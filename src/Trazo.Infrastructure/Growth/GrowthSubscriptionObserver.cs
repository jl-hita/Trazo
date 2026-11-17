using Trazo.Domain.Growth;
using Trazo.Infrastructure.Billing;

namespace Trazo.Infrastructure.Growth;

/// <summary>
/// Turns verified subscription changes into funnel events. They come from what Google Play reported to the backend, so the
/// economic part of the funnel can never be forged from the app. Every event carries a dedupe key: duplicate or
/// out-of-order notifications never count twice.
/// </summary>
public sealed class GrowthSubscriptionObserver(IGrowthEvents events) : ISubscriptionObserver
{
    public async Task OnChangedAsync(SubscriptionChange change, CancellationToken ct)
    {
        var row = change.Row;
        var plan = new GrowthEventInfo(Plan: row.ProductId);
        GrowthEventInfo Key(string key) => plan with { DedupeKey = key };

        if (change.TrialStarted) await events.RecordAsync(GrowthEventNames.TrialStarted, row.UserId, Key($"trial:{row.Id}"), ct);
        if (change.FirstPaid) await events.RecordAsync(GrowthEventNames.FirstPurchase, row.UserId, Key($"first_purchase:{row.Id}"), ct);
        if (change.Renewed) await events.RecordAsync(GrowthEventNames.Renewed, row.UserId, Key($"renewed:{row.Id}:{row.LatestOrderId}"), ct);
        if (change.Canceled) await events.RecordAsync(GrowthEventNames.Canceled, row.UserId, Key($"canceled:{row.Id}"), ct);
        if (change.Expired) await events.RecordAsync(GrowthEventNames.Expired, row.UserId, Key($"expired:{row.Id}"), ct);
        if (change.Revoked) await events.RecordAsync(GrowthEventNames.Refunded, row.UserId, Key($"refunded:{row.Id}"), ct);
    }
}
