namespace Trazo.Domain.Fiscal;

public static class FiscalKinds
{
    public const string Income = "income";
    public const string Expense = "expense";
    public static readonly string[] All = [Income, Expense];
}

/// <summary>How VAT (IVA) applies to one ledger entry.</summary>
public static class VatTreatments
{
    /// <summary>Spanish VAT charged (income) or paid (expense) at <see cref="FiscalEntry.VatRatePct"/>.</summary>
    public const string Domestic = "domestic";
    /// <summary>Business-to-business service with an EU counterparty (for example Google Ireland): no VAT on the invoice, reverse charge. Goes in the intra-EU return (modelo 349).</summary>
    public const string EuReverseCharge = "eu_reverse_charge";
    /// <summary>No VAT at all: exempt or not subject (self-employed social-security fee, insurance, bank charges, non-EU suppliers).</summary>
    public const string NoVat = "no_vat";
    public static readonly string[] All = [Domestic, EuReverseCharge, NoVat];
}

public static class FiscalSources
{
    public const string Manual = "manual";
    /// <summary>Income entry created from the estimate that TRAZO computes from verified Google Play purchases.</summary>
    public const string GoogleEstimate = "google_estimate";
}

/// <summary>One line of the income or expense ledger (libro de ingresos / gastos). Amounts in euros.</summary>
public sealed class FiscalEntry
{
    public Guid Id { get; set; }
    public required string Kind { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Accounting month, <c>yyyy-MM</c>, always derived from <see cref="Date"/>.</summary>
    public required string Period { get; set; }
    public required string Concept { get; set; }
    public string? Counterparty { get; set; }
    public string? CounterpartyTaxId { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country of the counterparty.</summary>
    public string? CounterpartyCountry { get; set; }
    public string? InvoiceNumber { get; set; }
    public required string Category { get; set; }
    public decimal Base { get; set; }
    public required string VatTreatment { get; set; }
    public decimal VatRatePct { get; set; }
    /// <summary>VAT amount: charged (income), paid (expense) or self-assessed (expense with reverse charge).</summary>
    public decimal VatAmount { get; set; }
    /// <summary>IRPF withholding applied by the payer to an income (retención soportada).</summary>
    public decimal WithholdingPct { get; set; }
    public decimal WithholdingAmount { get; set; }
    /// <summary>Share of an expense that is deductible, 0-100 (for mixed-use costs).</summary>
    public decimal DeductiblePct { get; set; } = 100;
    /// <summary>Cash amount that is paid or received.</summary>
    public decimal Total { get; set; }
    public string Source { get; set; } = FiscalSources.Manual;
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A closed accounting month. Entries of a closed month cannot be created, changed or deleted until it is reopened.</summary>
public sealed class FiscalClosure
{
    /// <summary>Accounting month, <c>yyyy-MM</c>.</summary>
    public required string Period { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
    public required string ClosedBy { get; set; }
    public int EntryCount { get; set; }
    /// <summary>JSON snapshot of the month's totals at the moment of closing.</summary>
    public required string SnapshotJson { get; set; }
}

/// <summary>Who closed or reopened a month, when and why.</summary>
public sealed class FiscalAuditEntry
{
    public Guid Id { get; set; }
    public required string Period { get; set; }
    /// <summary><c>closed</c> or <c>reopened</c>.</summary>
    public required string Action { get; set; }
    public required string Actor { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset At { get; set; }
}
