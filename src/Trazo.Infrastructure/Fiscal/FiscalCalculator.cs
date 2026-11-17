using System.Globalization;
using Trazo.Domain.Fiscal;

namespace Trazo.Infrastructure.Fiscal;

/// <summary>Settings of the owner's tax situation. Stored as one JSON value in <c>app_settings</c> (key <c>Fiscal:Profile</c>).</summary>
public sealed record FiscalProfile(
    string LegalName, string TaxId, string Address, string? GestorEmail,
    decimal DefaultVatPct, decimal IrpfAdvancePct, bool FilesModelo130,
    decimal PlayMonthlyPrice, decimal PlayAnnualPrice, decimal ConsumerVatPct, decimal GoogleFeePct)
{
    public const string SettingKey = "Fiscal:Profile";

    public static FiscalProfile Default { get; } = new(
        "", "", "", null, DefaultVatPct: 21m, IrpfAdvancePct: 20m, FilesModelo130: true,
        PlayMonthlyPrice: 6.99m, PlayAnnualPrice: 49.99m, ConsumerVatPct: 21m, GoogleFeePct: 15m);

    /// <summary>The name and tax id are needed on the documents handed to the accountant; without them a month cannot be closed.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(LegalName) && !string.IsNullOrWhiteSpace(TaxId);

    public static Dictionary<string, string[]> Validate(FiscalProfile p)
    {
        var errors = new Dictionary<string, string[]>();
        if (p.LegalName.Length > 150) errors["legalName"] = ["At most 150 characters."];
        if (p.TaxId.Length > 20) errors["taxId"] = ["At most 20 characters."];
        if (p.Address.Length > 300) errors["address"] = ["At most 300 characters."];
        if (p.GestorEmail is { Length: > 0 } email && (email.Length > 200 || !email.Contains('@'))) errors["gestorEmail"] = ["Not a valid email address."];
        Percent(errors, "defaultVatPct", p.DefaultVatPct, 0, 30);
        Percent(errors, "irpfAdvancePct", p.IrpfAdvancePct, 0, 50);
        Percent(errors, "consumerVatPct", p.ConsumerVatPct, 0, 30);
        Percent(errors, "googleFeePct", p.GoogleFeePct, 0, 50);
        if (p.PlayMonthlyPrice is <= 0 or > 1000) errors["playMonthlyPrice"] = ["Between 0 and 1000 €."];
        if (p.PlayAnnualPrice is <= 0 or > 10000) errors["playAnnualPrice"] = ["Between 0 and 10000 €."];
        return errors;
    }

    private static void Percent(Dictionary<string, string[]> errors, string name, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max) errors[name] = [$"Between {min} and {max}."];
    }
}

/// <summary>Month identifiers: <c>yyyy-MM</c>.</summary>
public static class FiscalPeriod
{
    public static bool TryParse(string? value, out int year, out int month)
    {
        year = month = 0;
        if (value is not { Length: 7 } || value[4] != '-') return false;
        return int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && year is >= 2020 and <= 2100 && month is >= 1 and <= 12;
    }

    public static string Of(DateOnly date) => Of(date.Year, date.Month);
    public static string Of(int year, int month) => $"{year:D4}-{month:D2}";
    public static int QuarterOf(int month) => (month - 1) / 3 + 1;

    /// <summary>The three months of a quarter.</summary>
    public static string[] QuarterMonths(int year, int quarter) =>
        Enumerable.Range((quarter - 1) * 3 + 1, 3).Select(m => Of(year, m)).ToArray();

    /// <summary>Statutory filing deadline of the quarterly returns (303, 130, 349): the 20th after the quarter, the 30th of January for the fourth.</summary>
    public static DateOnly DueDate(int year, int quarter) => quarter switch
    {
        1 => new DateOnly(year, 4, 20),
        2 => new DateOnly(year, 7, 20),
        3 => new DateOnly(year, 10, 20),
        _ => new DateOnly(year + 1, 1, 30)
    };
}

public sealed record FiscalTotals(
    int IncomeCount, int ExpenseCount,
    decimal IncomeBase, decimal IncomeDomesticBase, decimal IncomeEuB2bBase, decimal IncomeNoVatBase,
    decimal VatOnIncome, decimal SelfAssessedVat, decimal VatOutput,
    decimal ExpenseBase, decimal DeductibleCost, decimal VatInput, decimal VatBalance,
    decimal WithholdingsSuffered, decimal NetResult);

public sealed record Intra349Row(string Key, string? Counterparty, string? TaxId, string? Country, decimal Base);

public sealed record Modelo303(
    decimal DomesticIncomeBase, decimal VatOnIncome, decimal SelfAssessedBase, decimal SelfAssessedVat,
    decimal VatOutput, decimal DeductibleBase, decimal VatInput, decimal Balance, decimal NotSubjectEuB2bBase);

public sealed record Modelo130(
    bool Applies, decimal YtdIncome, decimal YtdDeductibleCost, decimal NetYtd, decimal Pct, decimal Gross,
    decimal WithholdingsYtd, decimal PreviousPayments, decimal Result, decimal ToPay);

public sealed record QuarterSummary(
    int Year, int Quarter, string Label, DateOnly DueDate, FiscalTotals Totals, Modelo303 Iva, Modelo130 Irpf, IReadOnlyList<Intra349Row> Intra349);

/// <summary>
/// The arithmetic of the books. Pure functions over ledger entries: VAT per treatment, month totals and the orientative
/// figures of the quarterly returns. They are a help for the accountant, not the filing itself.
/// </summary>
public static class FiscalCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Fills the computed amounts (VAT, withholding and cash total) of an entry from its base and treatment.</summary>
    public static void Apply(FiscalEntry e)
    {
        var income = e.Kind == FiscalKinds.Income;
        if (e.VatTreatment == VatTreatments.Domestic || (!income && e.VatTreatment == VatTreatments.EuReverseCharge))
            e.VatAmount = Round(e.Base * e.VatRatePct / 100m);
        else
        {
            e.VatAmount = 0;
            if (e.VatTreatment != VatTreatments.EuReverseCharge) e.VatRatePct = 0;
        }
        if (!income) { e.WithholdingPct = 0; e.WithholdingAmount = 0; }
        else e.WithholdingAmount = Round(e.Base * e.WithholdingPct / 100m);
        if (income) e.DeductiblePct = 100;

        e.Total = income
            ? e.Base + (e.VatTreatment == VatTreatments.Domestic ? e.VatAmount : 0) - e.WithholdingAmount
            : e.Base + (e.VatTreatment == VatTreatments.Domestic ? e.VatAmount : 0);
    }

    /// <summary>Share of the entry's VAT that can be deducted.</summary>
    private static decimal DeductibleShare(FiscalEntry e) => e.DeductiblePct / 100m;

    public static FiscalTotals Totals(IEnumerable<FiscalEntry> entries)
    {
        var list = entries as IReadOnlyCollection<FiscalEntry> ?? entries.ToList();
        var income = list.Where(e => e.Kind == FiscalKinds.Income).ToList();
        var expenses = list.Where(e => e.Kind == FiscalKinds.Expense).ToList();

        var incomeBase = income.Sum(e => e.Base);
        var domestic = income.Where(e => e.VatTreatment == VatTreatments.Domestic).Sum(e => e.Base);
        var eu = income.Where(e => e.VatTreatment == VatTreatments.EuReverseCharge).Sum(e => e.Base);
        var noVat = income.Where(e => e.VatTreatment == VatTreatments.NoVat).Sum(e => e.Base);
        var vatOnIncome = income.Where(e => e.VatTreatment == VatTreatments.Domestic).Sum(e => e.VatAmount);
        var selfAssessed = expenses.Where(e => e.VatTreatment == VatTreatments.EuReverseCharge).Sum(e => e.VatAmount);

        var expenseBase = expenses.Sum(e => e.Base);
        var vatInput = Round(expenses.Where(e => e.VatTreatment != VatTreatments.NoVat).Sum(e => e.VatAmount * DeductibleShare(e)));
        // For income tax a cost is the deductible share of the base plus the VAT that could not be deducted.
        var cost = Round(expenses.Sum(e => e.Base * DeductibleShare(e)
            + (e.VatTreatment == VatTreatments.NoVat ? 0 : e.VatAmount * (1 - DeductibleShare(e)))));
        var withholdings = income.Sum(e => e.WithholdingAmount);
        var output = vatOnIncome + selfAssessed;

        return new FiscalTotals(income.Count, expenses.Count, incomeBase, domestic, eu, noVat,
            vatOnIncome, selfAssessed, output, expenseBase, cost, vatInput, output - vatInput, withholdings, incomeBase - cost);
    }

    /// <summary>Orientative figures of one quarter from every entry of the year up to the end of that quarter.</summary>
    public static QuarterSummary Quarter(IReadOnlyCollection<FiscalEntry> yearEntries, int year, int quarter, FiscalProfile profile)
    {
        var months = FiscalPeriod.QuarterMonths(year, quarter);
        var inQuarter = yearEntries.Where(e => months.Contains(e.Period)).ToList();
        var totals = Totals(inQuarter);

        var iva = new Modelo303(
            totals.IncomeDomesticBase, totals.VatOnIncome,
            inQuarter.Where(e => e.Kind == FiscalKinds.Expense && e.VatTreatment == VatTreatments.EuReverseCharge).Sum(e => e.Base), totals.SelfAssessedVat,
            totals.VatOutput,
            inQuarter.Where(e => e.Kind == FiscalKinds.Expense && e.VatTreatment != VatTreatments.NoVat).Sum(e => e.Base * DeductibleShare(e)), totals.VatInput,
            totals.VatBalance, totals.IncomeEuB2bBase);

        var irpf = Irpf(yearEntries, year, quarter, profile);

        var rows = inQuarter.Where(e => e.VatTreatment == VatTreatments.EuReverseCharge)
            .GroupBy(e => (Key: e.Kind == FiscalKinds.Income ? "S" : "A", e.CounterpartyTaxId, e.Counterparty, e.CounterpartyCountry))
            .Select(g => new Intra349Row(g.Key.Key, g.Key.Counterparty, g.Key.CounterpartyTaxId, g.Key.CounterpartyCountry, g.Sum(e => e.Base)))
            .OrderBy(r => r.Key).ThenBy(r => r.Counterparty).ToList();

        return new QuarterSummary(year, quarter, $"{year}T{quarter}", FiscalPeriod.DueDate(year, quarter), totals, iva, irpf, rows);
    }

    /// <summary>
    /// Modelo 130 (quarterly income-tax instalment, direct estimation): the instalment is a percentage of the net result of the
    /// year to date, minus the withholdings suffered and the instalments already paid in previous quarters.
    /// </summary>
    private static Modelo130 Irpf(IReadOnlyCollection<FiscalEntry> yearEntries, int year, int quarter, FiscalProfile profile)
    {
        if (!profile.FilesModelo130)
            return new Modelo130(false, 0, 0, 0, profile.IrpfAdvancePct, 0, 0, 0, 0, 0);
        var ytdMonths = Enumerable.Range(1, quarter * 3).Select(m => FiscalPeriod.Of(year, m)).ToHashSet();
        var ytd = Totals(yearEntries.Where(e => ytdMonths.Contains(e.Period)));
        var net = ytd.IncomeBase - ytd.DeductibleCost;
        var gross = Round(Math.Max(0, net) * profile.IrpfAdvancePct / 100m);
        var previous = 0m;
        for (var q = 1; q < quarter; q++) previous += Irpf(yearEntries, year, q, profile).ToPay;
        var result = gross - ytd.WithholdingsSuffered - previous;
        return new Modelo130(true, ytd.IncomeBase, ytd.DeductibleCost, net, profile.IrpfAdvancePct, gross,
            ytd.WithholdingsSuffered, previous, result, Math.Max(0, result));
    }
}
