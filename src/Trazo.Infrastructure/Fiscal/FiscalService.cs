using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Billing;
using Trazo.Domain.Fiscal;
using Trazo.Domain.Growth;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Fiscal;

/// <summary>A refusal with the HTTP status it maps to. <see cref="Code"/> is stable so that the console can react to it.</summary>
public sealed class FiscalException(int status, string code, string message, IDictionary<string, string[]>? errors = null, object? detail = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;
    public object? Detail { get; } = detail;
}

public sealed record FiscalEntryInput(
    string? Kind, DateOnly? Date, string? Concept, string? Counterparty, string? CounterpartyTaxId, string? CounterpartyCountry,
    string? InvoiceNumber, string? Category, decimal? Base, string? VatTreatment, decimal? VatRatePct, decimal? WithholdingPct,
    decimal? DeductiblePct, string? Notes);

public sealed record FiscalWarning(string Code, string Message, int Count);

public sealed record FiscalMonthRow(string Period, bool Closed, DateTimeOffset? ClosedAt, int IncomeCount, int ExpenseCount, decimal IncomeBase,
    decimal DeductibleCost, decimal VatBalance, bool Finished);

public sealed record FiscalQuarterRow(int Quarter, string Label, DateOnly DueDate, bool AllClosed, decimal VatBalance, decimal IrpfToPay, bool Overdue);

public sealed record FiscalYearView(int Year, bool ProfileComplete, string CurrentPeriod, IReadOnlyList<FiscalMonthRow> Months,
    IReadOnlyList<FiscalQuarterRow> Quarters, FiscalTotals Totals);

public sealed record FiscalMonthView(string Period, bool Closed, FiscalClosure? Closure, IReadOnlyList<FiscalEntry> Entries, FiscalTotals Totals,
    GoogleEstimate Estimate, bool HasGoogleIncome, IReadOnlyList<FiscalWarning> Warnings, bool CanClose, string? CannotCloseReason);

public sealed record FiscalPackage(string FileName, byte[] Content);

public interface IFiscalService
{
    Task<FiscalProfile> GetProfileAsync(CancellationToken ct);
    Task<FiscalProfile> SaveProfileAsync(FiscalProfile profile, CancellationToken ct);
    Task<FiscalYearView> GetYearAsync(int year, CancellationToken ct);
    Task<FiscalMonthView> GetMonthAsync(string period, CancellationToken ct);
    Task<QuarterSummary> GetQuarterAsync(int year, int quarter, CancellationToken ct);
    Task<FiscalEntry> CreateEntryAsync(FiscalEntryInput input, string actor, CancellationToken ct);
    Task<FiscalEntry> UpdateEntryAsync(Guid id, FiscalEntryInput input, CancellationToken ct);
    Task DeleteEntryAsync(Guid id, CancellationToken ct);
    Task<FiscalEntry> BookGoogleEstimateAsync(string period, string actor, CancellationToken ct);
    Task<FiscalMonthView> CloseMonthAsync(string period, bool acknowledgeWarnings, string actor, CancellationToken ct);
    Task ReopenMonthAsync(string period, string? reason, string actor, CancellationToken ct);
    Task<FiscalPackage> GetMonthPackageAsync(string period, CancellationToken ct);
    Task<FiscalPackage> GetQuarterPackageAsync(int year, int quarter, CancellationToken ct);
    Task<IReadOnlyList<FiscalAuditEntry>> GetAuditAsync(CancellationToken ct);
}

/// <summary>
/// The tax books of the owner (autónomo): income and expense ledgers, VAT and income-tax figures per month and quarter, and
/// the month-closing that freezes a month and produces the package for the accountant. Superadmin only. Figures are
/// orientative: they prepare the work of the accountant and never file anything.
/// </summary>
public sealed class FiscalService(TrazoDbContext db, TimeProvider clock) : IFiscalService
{
    public static readonly string[] IncomeCategories = ["google_play", "other_income"];
    public static readonly string[] ExpenseCategories =
        ["hosting", "software", "store_fees", "domains", "advertising", "professional_services", "self_employed_fee", "equipment", "office", "training", "taxes_fees", "other"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string GoogleIrelandName = "Google Ireland Limited";
    private const string GoogleIrelandVatId = "IE6388047V";

    // ------------------------------------------------------------------ profile

    public async Task<FiscalProfile> GetProfileAsync(CancellationToken ct)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == FiscalProfile.SettingKey, ct);
        if (row is null) return FiscalProfile.Default;
        try { return JsonSerializer.Deserialize<FiscalProfile>(row.Value, Json) ?? FiscalProfile.Default; }
        catch (JsonException) { return FiscalProfile.Default; }
    }

    public async Task<FiscalProfile> SaveProfileAsync(FiscalProfile input, CancellationToken ct)
    {
        var profile = input with
        {
            LegalName = (input.LegalName ?? "").Trim(), TaxId = (input.TaxId ?? "").Trim().ToUpperInvariant(),
            Address = (input.Address ?? "").Trim(), GestorEmail = string.IsNullOrWhiteSpace(input.GestorEmail) ? null : input.GestorEmail.Trim()
        };
        var errors = FiscalProfile.Validate(profile);
        if (errors.Count > 0) throw new FiscalException(400, "validation", "The fiscal profile is not valid.", errors);

        var value = JsonSerializer.Serialize(profile, Json);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == FiscalProfile.SettingKey, ct);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = FiscalProfile.SettingKey, Value = value, IsSecret = false, UpdatedAt = clock.GetUtcNow() });
        else { row.Value = value; row.UpdatedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
        return profile;
    }

    // ------------------------------------------------------------------ reading

    private string CurrentPeriod() => FiscalPeriod.Of(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    private static bool Finished(string period, string current) => string.CompareOrdinal(period, current) < 0;

    private async Task<List<FiscalEntry>> YearEntriesAsync(int year, CancellationToken ct)
    {
        var prefix = $"{year:D4}-";
        return await db.FiscalEntries.AsNoTracking().Where(e => e.Period.StartsWith(prefix)).OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ToListAsync(ct);
    }

    public async Task<FiscalYearView> GetYearAsync(int year, CancellationToken ct)
    {
        if (year is < 2020 or > 2100) throw new FiscalException(400, "validation", "The year is not valid.");
        var profile = await GetProfileAsync(ct);
        var entries = await YearEntriesAsync(year, ct);
        var prefix = $"{year:D4}-";
        var closures = await db.FiscalClosures.AsNoTracking().Where(c => c.Period.StartsWith(prefix)).ToDictionaryAsync(c => c.Period, ct);
        var current = CurrentPeriod();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var months = Enumerable.Range(1, 12).Select(m =>
        {
            var period = FiscalPeriod.Of(year, m);
            var totals = FiscalCalculator.Totals(entries.Where(e => e.Period == period));
            closures.TryGetValue(period, out var closure);
            return new FiscalMonthRow(period, closure is not null, closure?.ClosedAt, totals.IncomeCount, totals.ExpenseCount,
                totals.IncomeBase, totals.DeductibleCost, totals.VatBalance, Finished(period, current));
        }).ToList();

        var quarters = Enumerable.Range(1, 4).Select(q =>
        {
            var summary = FiscalCalculator.Quarter(entries, year, q, profile);
            var allClosed = FiscalPeriod.QuarterMonths(year, q).All(closures.ContainsKey);
            var overdue = !allClosed && summary.DueDate < today;
            return new FiscalQuarterRow(q, summary.Label, summary.DueDate, allClosed, summary.Totals.VatBalance, summary.Irpf.ToPay, overdue);
        }).ToList();

        return new FiscalYearView(year, profile.IsComplete, current, months, quarters, FiscalCalculator.Totals(entries));
    }

    public async Task<QuarterSummary> GetQuarterAsync(int year, int quarter, CancellationToken ct)
    {
        if (quarter is < 1 or > 4 || year is < 2020 or > 2100) throw new FiscalException(400, "validation", "The quarter is not valid.");
        return FiscalCalculator.Quarter(await YearEntriesAsync(year, ct), year, quarter, await GetProfileAsync(ct));
    }

    public async Task<FiscalMonthView> GetMonthAsync(string period, CancellationToken ct)
    {
        var (year, _) = ParsePeriod(period);
        var profile = await GetProfileAsync(ct);
        var entries = (await YearEntriesAsync(year, ct)).Where(e => e.Period == period).ToList();
        var closure = await db.FiscalClosures.AsNoTracking().FirstOrDefaultAsync(c => c.Period == period, ct);
        var estimate = await EstimateAsync(period, profile, ct);
        var hasGoogle = entries.Any(e => e.Kind == FiscalKinds.Income && e.Category == "google_play");
        var warnings = await WarningsAsync(period, entries, estimate, hasGoogle, ct);

        string? reason = null;
        if (closure is not null) reason = "already_closed";
        else if (!Finished(period, CurrentPeriod())) reason = "month_not_finished";
        else if (!profile.IsComplete) reason = "profile_incomplete";

        return new FiscalMonthView(period, closure is not null, closure, entries, FiscalCalculator.Totals(entries), estimate, hasGoogle,
            warnings, reason is null, reason);
    }

    private async Task<IReadOnlyList<FiscalWarning>> WarningsAsync(string period, IReadOnlyList<FiscalEntry> entries, GoogleEstimate estimate,
        bool hasGoogleIncome, CancellationToken ct)
    {
        var list = new List<FiscalWarning>();
        if (entries.Count == 0)
            list.Add(new("no_entries", "The month has no entries at all.", 0));
        if (estimate.Available && !hasGoogleIncome)
            list.Add(new("missing_google_income", "TRAZO recorded Google Play purchases this month but no Google Play income has been booked.", estimate.Charges));

        var earlier = await db.FiscalEntries.AsNoTracking().Where(e => string.Compare(e.Period, period) < 0).Select(e => e.Period).Distinct().ToListAsync(ct);
        var closed = (await db.FiscalClosures.AsNoTracking().Select(c => c.Period).ToListAsync(ct)).ToHashSet();
        var open = earlier.Count(p => !closed.Contains(p));
        if (open > 0)
            list.Add(new("earlier_months_open", "Earlier months with entries are still open; close them in order.", open));

        var noInvoice = entries.Count(e => e.VatTreatment == VatTreatments.Domestic && string.IsNullOrWhiteSpace(e.InvoiceNumber));
        if (noInvoice > 0)
            list.Add(new("missing_invoice_number", "Entries with Spanish VAT but no invoice number: the VAT cannot be deducted without an invoice.", noInvoice));
        var noTaxId = entries.Count(e => e.VatTreatment == VatTreatments.EuReverseCharge && string.IsNullOrWhiteSpace(e.CounterpartyTaxId));
        if (noTaxId > 0)
            list.Add(new("missing_eu_vat_id", "Intra-EU entries without the counterparty's VAT number (needed for modelo 349).", noTaxId));
        return list;
    }

    // ------------------------------------------------------------------ Google Play estimate

    private async Task<GoogleEstimate> EstimateAsync(string period, FiscalProfile profile, CancellationToken ct)
    {
        var (year, month) = ParsePeriod(period);
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var names = new[] { GrowthEventNames.FirstPurchase, GrowthEventNames.Renewed, GrowthEventNames.Refunded };
        var events = await db.GrowthEvents.AsNoTracking().Where(e => names.Contains(e.Name) && e.DedupeKey != null)
            .Select(e => new { e.Name, e.OccurredAt, e.DedupeKey }).ToListAsync(ct);

        var parsed = events.Select(e => (e.Name, e.OccurredAt, Id: ParseSubscriptionId(e.DedupeKey))).Where(e => e.Id is not null).ToList();
        var ids = parsed.Select(e => e.Id!.Value).Distinct().ToList();
        var rows = await db.Subscriptions.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && !s.IsTestPurchase && s.Source == SubscriptionSources.GooglePlay)
            .Select(s => new { s.Id, s.StartedAt, s.ExpiresAt }).ToListAsync(ct);
        var spans = rows.ToDictionary(r => r.Id, r => (r.ExpiresAt - r.StartedAt).TotalDays);

        var sales = parsed.Where(e => spans.ContainsKey(e.Id!.Value))
            .Select(e => new SaleObservation(e.Id!.Value, ToEstimatorEvent(e.Name), e.OccurredAt, spans[e.Id!.Value])).ToList();
        return FiscalEstimator.Estimate(sales, from, from.AddMonths(1), profile);
    }

    private static string ToEstimatorEvent(string name) => name switch
    {
        GrowthEventNames.FirstPurchase => FiscalEstimator.FirstPurchase,
        GrowthEventNames.Renewed => FiscalEstimator.Renewed,
        _ => FiscalEstimator.Refunded
    };

    /// <summary>Dedupe keys are <c>first_purchase:{id}</c>, <c>renewed:{id}:{order}</c> and <c>refunded:{id}</c>.</summary>
    private static Guid? ParseSubscriptionId(string? key)
    {
        var parts = key?.Split(':');
        return parts is { Length: >= 2 } && Guid.TryParse(parts[1], out var id) ? id : null;
    }

    public async Task<FiscalEntry> BookGoogleEstimateAsync(string period, string actor, CancellationToken ct)
    {
        var (year, month) = ParsePeriod(period);
        await EnsureOpenAsync(period, ct);
        var profile = await GetProfileAsync(ct);
        var estimate = await EstimateAsync(period, profile, ct);
        if (!estimate.Available) throw new FiscalException(409, "no_estimate", "TRAZO has no Google Play purchases recorded for that month.");
        if (await db.FiscalEntries.AnyAsync(e => e.Period == period && e.Kind == FiscalKinds.Income && e.Category == "google_play", ct))
            throw new FiscalException(409, "already_booked", "Google Play income is already booked for that month.");

        var entry = new FiscalEntry
        {
            Id = Guid.NewGuid(), Kind = FiscalKinds.Income, Period = period,
            Date = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            Concept = $"Google Play - ventas de {period} (estimacion)", Counterparty = GoogleIrelandName, CounterpartyTaxId = GoogleIrelandVatId,
            CounterpartyCountry = "IE", Category = "google_play", Base = estimate.Income, VatTreatment = VatTreatments.EuReverseCharge,
            Source = FiscalSources.GoogleEstimate,
            Notes = "Estimacion a partir de las compras verificadas. Sustituir por el importe del informe de ingresos de Google Play.",
            CreatedBy = actor, CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow()
        };
        FiscalCalculator.Apply(entry);
        db.FiscalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    // ------------------------------------------------------------------ entries

    private static (int Year, int Month) ParsePeriod(string? period)
    {
        if (!FiscalPeriod.TryParse(period, out var year, out var month))
            throw new FiscalException(400, "validation", "The month must be written yyyy-MM.");
        return (year, month);
    }

    private async Task EnsureOpenAsync(string period, CancellationToken ct)
    {
        if (await db.FiscalClosures.AnyAsync(c => c.Period == period, ct))
            throw new FiscalException(409, "month_closed", $"{period} is closed. Reopen it to change it.");
    }

    public async Task<FiscalEntry> CreateEntryAsync(FiscalEntryInput input, string actor, CancellationToken ct)
    {
        var profile = await GetProfileAsync(ct);
        var entry = new FiscalEntry { Id = Guid.NewGuid(), Kind = "", Period = "", Concept = "", Category = "", VatTreatment = "", CreatedBy = actor, CreatedAt = clock.GetUtcNow() };
        Fill(entry, input, profile);
        await EnsureOpenAsync(entry.Period, ct);
        db.FiscalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<FiscalEntry> UpdateEntryAsync(Guid id, FiscalEntryInput input, CancellationToken ct)
    {
        var entry = await db.FiscalEntries.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new FiscalException(404, "not_found", "The entry does not exist.");
        await EnsureOpenAsync(entry.Period, ct);
        Fill(entry, input, await GetProfileAsync(ct));
        await EnsureOpenAsync(entry.Period, ct);
        // An edited estimate is a figure the owner has checked: it stops being flagged as an estimate.
        if (entry.Source == FiscalSources.GoogleEstimate) entry.Source = FiscalSources.Manual;
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task DeleteEntryAsync(Guid id, CancellationToken ct)
    {
        var entry = await db.FiscalEntries.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new FiscalException(404, "not_found", "The entry does not exist.");
        await EnsureOpenAsync(entry.Period, ct);
        db.FiscalEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    private void Fill(FiscalEntry e, FiscalEntryInput i, FiscalProfile profile)
    {
        var errors = new Dictionary<string, string[]>();
        var kind = i.Kind?.Trim().ToLowerInvariant() ?? "";
        if (!FiscalKinds.All.Contains(kind)) errors["kind"] = ["Must be income or expense."];
        if (i.Date is null || i.Date.Value.Year is < 2020 or > 2100) errors["date"] = ["A date between 2020 and 2100 is required."];
        var concept = i.Concept?.Trim() ?? "";
        if (concept.Length is 0 or > 200) errors["concept"] = ["Between 1 and 200 characters."];
        var category = i.Category?.Trim().ToLowerInvariant() ?? "";
        var categories = kind == FiscalKinds.Income ? IncomeCategories : ExpenseCategories;
        if (!categories.Contains(category)) errors["category"] = [$"One of: {string.Join(", ", categories)}."];
        if (i.Base is null || i.Base == 0 || Math.Abs(i.Base.Value) > 10_000_000m) errors["base"] = ["A non-zero amount up to 10,000,000 (negative for a credit note)."];
        var treatment = i.VatTreatment?.Trim().ToLowerInvariant() ?? "";
        if (!VatTreatments.All.Contains(treatment)) errors["vatTreatment"] = [$"One of: {string.Join(", ", VatTreatments.All)}."];

        var needsRate = treatment == VatTreatments.Domestic || (kind == FiscalKinds.Expense && treatment == VatTreatments.EuReverseCharge);
        var rate = i.VatRatePct ?? (needsRate ? profile.DefaultVatPct : 0);
        if (rate is < 0 or > 30) errors["vatRatePct"] = ["Between 0 and 30."];
        var withholding = i.WithholdingPct ?? 0;
        if (withholding is < 0 or > 50) errors["withholdingPct"] = ["Between 0 and 50."];
        var deductible = i.DeductiblePct ?? 100;
        if (deductible is < 0 or > 100) errors["deductiblePct"] = ["Between 0 and 100."];

        var country = i.CounterpartyCountry?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(country) && (country.Length != 2 || !country.All(char.IsAsciiLetter))) errors["counterpartyCountry"] = ["Two-letter country code."];
        Max(errors, "counterparty", i.Counterparty, 150); Max(errors, "counterpartyTaxId", i.CounterpartyTaxId, 30);
        Max(errors, "invoiceNumber", i.InvoiceNumber, 60); Max(errors, "notes", i.Notes, 500);
        if (errors.Count > 0) throw new FiscalException(400, "validation", "The entry is not valid.", errors);

        e.Kind = kind; e.Date = i.Date!.Value; e.Period = FiscalPeriod.Of(e.Date); e.Concept = concept; e.Category = category;
        e.Counterparty = Clean(i.Counterparty); e.CounterpartyTaxId = Clean(i.CounterpartyTaxId)?.ToUpperInvariant();
        e.CounterpartyCountry = string.IsNullOrEmpty(country) ? null : country; e.InvoiceNumber = Clean(i.InvoiceNumber); e.Notes = Clean(i.Notes);
        e.Base = FiscalCalculator.Round(i.Base!.Value); e.VatTreatment = treatment; e.VatRatePct = rate;
        e.WithholdingPct = withholding; e.DeductiblePct = deductible;
        FiscalCalculator.Apply(e);
        e.UpdatedAt = clock.GetUtcNow();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Max(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max) errors[field] = [$"At most {max} characters."];
    }

    // ------------------------------------------------------------------ closing

    public async Task<FiscalMonthView> CloseMonthAsync(string period, bool acknowledgeWarnings, string actor, CancellationToken ct)
    {
        ParsePeriod(period);
        var view = await GetMonthAsync(period, ct);
        if (view.CannotCloseReason is { } reason)
        {
            var message = reason switch
            {
                "already_closed" => "The month is already closed.",
                "month_not_finished" => "A month can only be closed once it has finished.",
                _ => "Fill in the legal name and tax id (NIF) in the fiscal profile first."
            };
            throw new FiscalException(reason == "profile_incomplete" ? 422 : 409, reason, message);
        }
        if (view.Warnings.Count > 0 && !acknowledgeWarnings)
            throw new FiscalException(409, "warnings", "The month has warnings. Review them or close anyway.", detail: view.Warnings);

        var now = clock.GetUtcNow();
        db.FiscalClosures.Add(new FiscalClosure
        {
            Period = period, ClosedAt = now, ClosedBy = actor, EntryCount = view.Entries.Count, SnapshotJson = JsonSerializer.Serialize(view.Totals, Json)
        });
        db.FiscalAudit.Add(new FiscalAuditEntry { Id = Guid.NewGuid(), Period = period, Action = "closed", Actor = actor, At = now,
            Reason = view.Warnings.Count > 0 ? "Closed with warnings: " + string.Join(", ", view.Warnings.Select(w => w.Code)) : null });
        await db.SaveChangesAsync(ct);
        return await GetMonthAsync(period, ct);
    }

    public async Task ReopenMonthAsync(string period, string? reason, string actor, CancellationToken ct)
    {
        ParsePeriod(period);
        var text = reason?.Trim() ?? "";
        if (text.Length is < 10 or > 300)
            throw new FiscalException(400, "validation", "Explain why in 10 to 300 characters.", new Dictionary<string, string[]> { ["reason"] = ["Between 10 and 300 characters."] });
        var closure = await db.FiscalClosures.FirstOrDefaultAsync(c => c.Period == period, ct)
            ?? throw new FiscalException(409, "not_closed", "The month is not closed.");
        db.FiscalClosures.Remove(closure);
        db.FiscalAudit.Add(new FiscalAuditEntry { Id = Guid.NewGuid(), Period = period, Action = "reopened", Actor = actor, Reason = text, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FiscalAuditEntry>> GetAuditAsync(CancellationToken ct) =>
        (await db.FiscalAudit.AsNoTracking().ToListAsync(ct)).OrderByDescending(a => a.At).Take(200).ToList();

    // ------------------------------------------------------------------ packages

    public async Task<FiscalPackage> GetMonthPackageAsync(string period, CancellationToken ct)
    {
        var (year, month) = ParsePeriod(period);
        var closure = await db.FiscalClosures.AsNoTracking().FirstOrDefaultAsync(c => c.Period == period, ct)
            ?? throw new FiscalException(409, "not_closed", "Close the month first: the package is made from a closed month.");
        return await BuildAsync(period, year, FiscalPeriod.QuarterOf(month), [period], [closure], ct);
    }

    public async Task<FiscalPackage> GetQuarterPackageAsync(int year, int quarter, CancellationToken ct)
    {
        if (quarter is < 1 or > 4 || year is < 2020 or > 2100) throw new FiscalException(400, "validation", "The quarter is not valid.");
        var months = FiscalPeriod.QuarterMonths(year, quarter);
        var closures = await db.FiscalClosures.AsNoTracking().Where(c => months.Contains(c.Period)).ToListAsync(ct);
        if (closures.Count != 3) throw new FiscalException(409, "quarter_not_closed", "Close the three months of the quarter first.");
        return await BuildAsync($"{year}T{quarter}", year, quarter, months, closures, ct);
    }

    private async Task<FiscalPackage> BuildAsync(string label, int year, int quarter, string[] periods, IReadOnlyList<FiscalClosure> closures, CancellationToken ct)
    {
        var profile = await GetProfileAsync(ct);
        var yearEntries = await YearEntriesAsync(year, ct);
        var inPackage = yearEntries.Where(e => periods.Contains(e.Period)).ToList();
        var summary = FiscalCalculator.Quarter(yearEntries, year, quarter, profile);
        var bytes = FiscalPackageBuilder.Build(label, profile, inPackage, closures, summary, clock.GetUtcNow());
        return new FiscalPackage($"trazo_gestor_{label}.zip", bytes);
    }
}
