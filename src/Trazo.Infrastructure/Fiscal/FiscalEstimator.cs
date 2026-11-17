namespace Trazo.Infrastructure.Fiscal;

/// <summary>A verified purchase event of one subscription, as the backend recorded it.</summary>
public sealed record SaleObservation(Guid SubscriptionId, string Event, DateTimeOffset At, double SpanDays);

public sealed record GoogleEstimate(
    int Charges, int MonthlyCharges, int AnnualCharges, int Refunds,
    decimal Gross, decimal NetOfVat, decimal GoogleFee, decimal Income, bool Available);

/// <summary>
/// Estimates what Google Play owes the developer for a month from the purchases the backend verified. It is an estimate:
/// prices vary by country and currency and the real figure is in Google's monthly earnings report, which is what must be
/// booked. It exists so that the month can be checked against Google's number and pre-filled.
/// </summary>
public static class FiscalEstimator
{
    public const string FirstPurchase = "first_purchase";
    public const string Renewed = "renewed";
    public const string Refunded = "refunded";

    public static GoogleEstimate Estimate(IReadOnlyList<SaleObservation> all, DateTimeOffset from, DateTimeOffset to, FiscalProfile profile)
    {
        var monthly = 0; var annual = 0; var refunds = 0;
        decimal gross = 0;
        foreach (var group in all.GroupBy(o => o.SubscriptionId))
        {
            var events = group.OrderBy(o => o.At).ToList();
            var paid = events.Where(o => o.Event is FirstPurchase or Renewed).ToList();
            for (var i = 0; i < paid.Count; i++)
            {
                var plan = PlanOf(paid, i);
                if (paid[i].At < from || paid[i].At >= to) continue;
                if (plan == "annual") { annual++; gross += profile.PlayAnnualPrice; } else { monthly++; gross += profile.PlayMonthlyPrice; }
            }
            foreach (var refund in events.Where(o => o.Event == Refunded && o.At >= from && o.At < to))
            {
                // The refunded charge is the last one before the refund (or the first one when none is recorded).
                var index = paid.FindLastIndex(o => o.At <= refund.At);
                var plan = paid.Count == 0 ? "monthly" : PlanOf(paid, Math.Max(0, index));
                refunds++;
                gross -= plan == "annual" ? profile.PlayAnnualPrice : profile.PlayMonthlyPrice;
            }
        }

        var net = FiscalCalculator.Round(gross / (1 + profile.ConsumerVatPct / 100m));
        var fee = FiscalCalculator.Round(net * profile.GoogleFeePct / 100m);
        var charges = monthly + annual;
        return new GoogleEstimate(charges, monthly, annual, refunds, FiscalCalculator.Round(gross), net, fee, net - fee, charges + refunds > 0);
    }

    /// <summary>Monthly or annual, from the gap to the neighbouring charge of the same subscription (Google reports both under one product).</summary>
    public static string PlanOf(IReadOnlyList<SaleObservation> paid, int index)
    {
        double? gap = null;
        if (index + 1 < paid.Count) gap = (paid[index + 1].At - paid[index].At).TotalDays;
        else if (index > 0) gap = (paid[index].At - paid[index - 1].At).TotalDays;
        gap ??= paid[index].SpanDays;
        return gap >= 200 ? "annual" : "monthly";
    }
}
