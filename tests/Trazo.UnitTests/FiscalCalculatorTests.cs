using Trazo.Domain.Fiscal;
using Trazo.Infrastructure.Fiscal;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The arithmetic of the tax books: VAT per treatment, totals, quarterly returns and the Google Play estimate.</summary>
public sealed class FiscalCalculatorTests
{
    private static FiscalEntry Entry(string kind, decimal @base, string treatment, string date = "2026-01-15", decimal rate = 21,
        decimal withholding = 0, decimal deductible = 100, string? taxId = null, string? name = null)
    {
        var d = DateOnly.Parse(date);
        var e = new FiscalEntry
        {
            Id = Guid.NewGuid(), Kind = kind, Date = d, Period = FiscalPeriod.Of(d), Concept = "x", Category = "other",
            Base = @base, VatTreatment = treatment, VatRatePct = rate, WithholdingPct = withholding, DeductiblePct = deductible,
            CounterpartyTaxId = taxId, Counterparty = name
        };
        FiscalCalculator.Apply(e);
        return e;
    }

    [Fact]
    public void DomesticExpense_ChargesVatAndDeductsIt()
    {
        var e = Entry(FiscalKinds.Expense, 100, VatTreatments.Domestic);
        Assert.Equal(21m, e.VatAmount);
        Assert.Equal(121m, e.Total);
        var t = FiscalCalculator.Totals([e]);
        Assert.Equal(21m, t.VatInput);
        Assert.Equal(100m, t.DeductibleCost);
        Assert.Equal(-21m, t.VatBalance);
    }

    [Fact]
    public void PartiallyDeductibleExpense_KeepsTheNonDeductibleVatAsCost()
    {
        var t = FiscalCalculator.Totals([Entry(FiscalKinds.Expense, 100, VatTreatments.Domestic, deductible: 50)]);
        Assert.Equal(10.5m, t.VatInput);
        Assert.Equal(60.5m, t.DeductibleCost); // 50 of base + 10.50 of VAT that could not be deducted
    }

    [Fact]
    public void EuReverseChargeExpense_SelfAssessesVatWithoutPayingIt()
    {
        var e = Entry(FiscalKinds.Expense, 100, VatTreatments.EuReverseCharge);
        Assert.Equal(21m, e.VatAmount);
        Assert.Equal(100m, e.Total);
        var t = FiscalCalculator.Totals([e]);
        Assert.Equal(21m, t.SelfAssessedVat);
        Assert.Equal(21m, t.VatInput);
        Assert.Equal(0m, t.VatBalance);
    }

    [Fact]
    public void EuB2bIncome_HasNoVatAndGoesInTheIntraEuReturn()
    {
        var e = Entry(FiscalKinds.Income, 1000, VatTreatments.EuReverseCharge, rate: 21, taxId: "IE6388047V", name: "Google Ireland Limited");
        Assert.Equal(0m, e.VatAmount);
        Assert.Equal(1000m, e.Total);
        var q = FiscalCalculator.Quarter([e], 2026, 1, FiscalProfile.Default);
        Assert.Equal(1000m, q.Iva.NotSubjectEuB2bBase);
        Assert.Equal(0m, q.Iva.VatOutput);
        var row = Assert.Single(q.Intra349);
        Assert.Equal("S", row.Key);
        Assert.Equal(1000m, row.Base);
    }

    [Fact]
    public void DomesticIncome_AppliesVatAndWithholding()
    {
        var e = Entry(FiscalKinds.Income, 1000, VatTreatments.Domestic, withholding: 15);
        Assert.Equal(210m, e.VatAmount);
        Assert.Equal(150m, e.WithholdingAmount);
        Assert.Equal(1060m, e.Total);
        Assert.Equal(150m, FiscalCalculator.Totals([e]).WithholdingsSuffered);
    }

    [Fact]
    public void NoVatEntries_IgnoreTheRate()
    {
        var e = Entry(FiscalKinds.Expense, 300, VatTreatments.NoVat, rate: 21);
        Assert.Equal(0m, e.VatAmount);
        Assert.Equal(0m, e.VatRatePct);
        Assert.Equal(300m, e.Total);
    }

    [Fact]
    public void IrpfInstalment_IsCumulativeAndSubtractsPreviousPayments()
    {
        var entries = new List<FiscalEntry>
        {
            Entry(FiscalKinds.Income, 10000, VatTreatments.EuReverseCharge, "2026-02-10"),
            Entry(FiscalKinds.Expense, 2000, VatTreatments.NoVat, "2026-03-10"),
            Entry(FiscalKinds.Income, 5000, VatTreatments.EuReverseCharge, "2026-05-10"),
            Entry(FiscalKinds.Expense, 1000, VatTreatments.NoVat, "2026-06-10")
        };
        var q1 = FiscalCalculator.Quarter(entries, 2026, 1, FiscalProfile.Default);
        Assert.Equal(1600m, q1.Irpf.ToPay);          // 20% of 8,000
        var q2 = FiscalCalculator.Quarter(entries, 2026, 2, FiscalProfile.Default);
        Assert.Equal(12000m, q2.Irpf.NetYtd);
        Assert.Equal(2400m, q2.Irpf.Gross);
        Assert.Equal(1600m, q2.Irpf.PreviousPayments);
        Assert.Equal(800m, q2.Irpf.ToPay);
    }

    [Fact]
    public void IrpfInstalment_NeverNegative_AndCanBeSwitchedOff()
    {
        var loss = new[] { Entry(FiscalKinds.Expense, 500, VatTreatments.NoVat) };
        Assert.Equal(0m, FiscalCalculator.Quarter(loss, 2026, 1, FiscalProfile.Default).Irpf.ToPay);
        var off = FiscalCalculator.Quarter(loss, 2026, 1, FiscalProfile.Default with { FilesModelo130 = false });
        Assert.False(off.Irpf.Applies);
    }

    [Theory]
    [InlineData(1, "2026-04-20")]
    [InlineData(2, "2026-07-20")]
    [InlineData(3, "2026-10-20")]
    [InlineData(4, "2027-01-30")]
    public void QuarterDueDates(int quarter, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), FiscalPeriod.DueDate(2026, quarter));

    [Theory]
    [InlineData("2026-10", true)]
    [InlineData("2026-13", false)]
    [InlineData("2026-1", false)]
    [InlineData("abcd-ef", false)]
    [InlineData(null, false)]
    public void PeriodParsing(string? value, bool valid) => Assert.Equal(valid, FiscalPeriod.TryParse(value, out _, out _));

    [Fact]
    public void GoogleEstimate_CountsChargesByPlanAndSubtractsRefunds()
    {
        var monthly = Guid.NewGuid(); var annual = Guid.NewGuid();
        var oct = (Day: 5, Month: 10);
        DateTimeOffset At(int month, int day) => new(2026, month, day, 10, 0, 0, TimeSpan.Zero);
        var sales = new List<SaleObservation>
        {
            new(monthly, FiscalEstimator.FirstPurchase, At(10, 5), 30), new(monthly, FiscalEstimator.Renewed, At(11, 5), 60),
            new(annual, FiscalEstimator.FirstPurchase, At(10, 10), 365)
        };
        var est = FiscalEstimator.Estimate(sales, At(10, 1), At(11, 1), FiscalProfile.Default);
        Assert.Equal((1, 1, 2), (est.MonthlyCharges, est.AnnualCharges, est.Charges));
        Assert.Equal(56.98m, est.Gross);
        Assert.Equal(47.09m, est.NetOfVat);
        Assert.Equal(7.06m, est.GoogleFee);
        Assert.Equal(40.03m, est.Income);

        sales.Add(new(annual, FiscalEstimator.Refunded, At(10, 20), 365));
        var refunded = FiscalEstimator.Estimate(sales, At(10, 1), At(11, 1), FiscalProfile.Default);
        Assert.Equal(1, refunded.Refunds);
        Assert.Equal(6.99m, refunded.Gross);
        _ = oct;
    }

    [Fact]
    public void GoogleEstimate_IsUnavailableWithoutSales() =>
        Assert.False(FiscalEstimator.Estimate([], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(30), FiscalProfile.Default).Available);

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-12,50", "-12,50")]
    [InlineData("a;b", "\"a;b\"")]
    public void CsvCells_AreQuotedAndFormulasNeutralised(string value, string expected) =>
        Assert.Equal(expected, FiscalPackageBuilder.Cell(value));
}
