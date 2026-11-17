using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Growth;

public enum DeferOutcome
{
    /// <summary>Google confirmed the new expiry: the reward is applied as a deferral.</summary>
    Deferred,
    /// <summary>Not possible now (trial, grace, not renewing...) or Google refused/failed: the reward stays pending, nothing is lost.</summary>
    StillPending,
    /// <summary>The reward was turned into a promotional window because the referrer's subscription no longer renews.</summary>
    Activated,
    NothingToDo
}

/// <summary>
/// Turns a referred user's first confirmed payment into the referrer's reward, and applies it.
/// <para>
/// A reward is **earned** when the backend has seen, from Google Play, the first real payment of the invited user (a trial or a
/// pending purchase never counts; a renewal never counts again). It is **applied** in one of two ways, and only one of them is
/// ever used per reward:
/// 1) the referrer pays for an auto-renewing subscription → the renewal is deferred with Google's API; only a deferral that Google
///    confirmed (new expiry read back) marks the reward as applied. If it cannot be done now the reward stays <c>pending</c>;
/// 2) the referrer has no auto-renewing subscription → a promotional window of the ledger (which starts after any paid period that
///    will not renew).
/// A pending reward becomes a promotional window the moment the subscription stops renewing, so it is never lost and never
/// overlaps a period the referrer is paying for.
/// </para>
/// </summary>
public interface IReferralRewardService
{
    Task ProcessConversionAsync(Subscription row, CancellationToken ct);
    Task HandleRevocationAsync(Subscription row, CancellationToken ct);
    /// <summary>Tries to apply every pending reward of the user (deferral or promotional window).</summary>
    Task ResolvePendingAsync(Guid userId, CancellationToken ct);
    Task<DeferOutcome> ResolveAsync(Guid grantId, CancellationToken ct);
}

public sealed class ReferralRewardService(
    TrazoDbContext db, IRuntimeSettings settings, IPremiumGrantService grants, IGrowthEvents events, IPlayBillingClient play,
    ISecretProtector protector, TimeProvider clock, ILogger<ReferralRewardService> logger) : IReferralRewardService
{
    /// <summary>A refund this long after the first payment no longer cancels the reward: the invited user was a real customer.</summary>
    public static readonly TimeSpan RefundWindow = TimeSpan.FromDays(60);

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserLocks = new();

    public async Task ProcessConversionAsync(Subscription row, CancellationToken ct)
    {
        // PaidAt is only set when Google reported a payment taken (never for a free trial or a pending purchase).
        if (row.PaidAt is null || row.Status is SubscriptionStatus.Revoked or SubscriptionStatus.Pending) return;
        var rel = await db.ReferralRelationships.AsNoTracking()
            .SingleOrDefaultAsync(r => r.ReferredUserId == row.UserId && r.Status == ReferralStatuses.Registered, ct);
        if (rel is null) return;

        var promotions = await settings.GetPromotionsAsync(ct);
        var now = clock.GetUtcNow();
        var plan = ReferralService.PlanOf(row);
        var rewarding = promotions.ReferralsEnabled;

        PremiumGrant? grant = null;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Single winner: only one request/notification can move the invitation out of "registered".
            var target = rewarding ? ReferralStatuses.Rewarded : ReferralStatuses.Skipped;
            var claimed = await db.ReferralRelationships.Where(r => r.Id == rel.Id && r.Status == ReferralStatuses.Registered)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, target).SetProperty(r => r.ConvertedAt, (DateTimeOffset?)now)
                    .SetProperty(r => r.ConversionSubscriptionId, (Guid?)row.Id).SetProperty(r => r.ConversionPlan, plan)
                    .SetProperty(r => r.UpdatedAt, now), ct);
            if (claimed != 1) { await tx.RollbackAsync(ct); return; }

            if (rewarding)
            {
                var renewing = await ReferrerRenewsAsync(rel.ReferrerUserId, now, ct);
                var result = await grants.GrantAsync(new GrantRequest(
                    rel.ReferrerUserId, PremiumGrantKinds.ReferralReward, rel.Id.ToString("N"), promotions.ReferralRewardDays,
                    OriginUserId: rel.ReferredUserId, OriginSubscriptionId: row.Id,
                    Status: renewing ? PremiumGrantStatuses.Pending : PremiumGrantStatuses.Active), ct);
                grant = result.Grant;
                await db.ReferralRelationships.Where(r => r.Id == rel.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.RewardGrantId, (Guid?)grant.Id).SetProperty(r => r.RewardDays, (int?)grant.Days), ct);
            }
            await tx.CommitAsync(ct);
        }

        if (grant is null) return;
        logger.LogInformation("Referral {ReferralId} converted; reward of {Days} days recorded for the referrer ({Status}).", rel.Id, grant.Days, grant.Status);
        await events.RecordAsync(GrowthEventNames.ReferralRewardGranted, rel.ReferrerUserId,
            new GrowthEventInfo(Channel: "referral", Reference: rel.Code, Plan: plan, DedupeKey: $"reward:{rel.Id:N}"), ct);
        if (grant.Status == PremiumGrantStatuses.Pending) await ResolveAsync(grant.Id, ct);
    }

    public async Task HandleRevocationAsync(Subscription row, CancellationToken ct)
    {
        var rel = await db.ReferralRelationships
            .SingleOrDefaultAsync(r => r.ConversionSubscriptionId == row.Id && r.Status == ReferralStatuses.Rewarded, ct);
        if (rel is null || rel.RewardGrantId is not { } grantId) return;
        var now = clock.GetUtcNow();
        if (rel.ConvertedAt is { } converted && now - converted > RefundWindow)
        {
            // A later refund: the invited user paid for a while, the reward stays. Left in the history for the administrator.
            rel.Incident = "late_refund_kept";
            rel.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }
        switch (await grants.VoidAsync(grantId, "The first payment of the invited user was refunded or revoked.", "system", ct))
        {
            case VoidOutcome.Voided:
                rel.Status = ReferralStatuses.Voided;
                rel.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                await events.RecordAsync(GrowthEventNames.ReferralRewardVoided, rel.ReferrerUserId,
                    new GrowthEventInfo(Channel: "referral", Reference: rel.Code, DedupeKey: $"reward_voided:{rel.Id:N}"), ct);
                break;
            case VoidOutcome.NotReversible:
                // Google cannot take a deferral back. The days stay applied and an administrator is told.
                rel.Incident = "reward_not_reversible";
                rel.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Referral {ReferralId}: the first payment was refunded but the reward was already deferred at Google; it cannot be reversed.", rel.Id);
                break;
        }
    }

    public async Task ResolvePendingAsync(Guid userId, CancellationToken ct)
    {
        var ids = await db.PremiumGrants.AsNoTracking().Where(g => g.UserId == userId && g.Status == PremiumGrantStatuses.Pending)
            .Select(g => g.Id).ToListAsync(ct);
        foreach (var id in ids) await ResolveAsync(id, ct);
    }

    public async Task<DeferOutcome> ResolveAsync(Guid grantId, CancellationToken ct)
    {
        var userId = await db.PremiumGrants.AsNoTracking().Where(g => g.Id == grantId).Select(g => (Guid?)g.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return DeferOutcome.NothingToDo;
        // One resolution per user at a time: two deferrals in parallel would race on Google's etag.
        var gate = UserLocks.GetOrAdd(userId.Value, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var grant = await db.PremiumGrants.SingleOrDefaultAsync(g => g.Id == grantId, ct);
            if (grant is null || grant.Status != PremiumGrantStatuses.Pending) return DeferOutcome.NothingToDo;
            var now = clock.GetUtcNow();
            var subs = (await db.Subscriptions.Where(s => s.UserId == grant.UserId).ToListAsync(ct))
                .Where(s => s.AutoRenewing && s.ExpiresAt > now && s.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod)
                .ToList();
            if (subs.Count == 0)
            {
                // The subscription no longer renews (or never existed): the reward becomes a window after any paid period.
                await grants.ActivatePendingAsync(grant.Id, ct);
                return DeferOutcome.Activated;
            }
            // Only a paid, active, auto-renewing subscription can be deferred; a trial or a grace period waits.
            var target = subs.Where(s => s.Status == SubscriptionStatus.Active).OrderByDescending(s => s.ExpiresAt).FirstOrDefault();
            if (target is null) return await NoteAsync(grant, "waiting_for_paid_subscription", ct);
            return await TryDeferAsync(grant, target, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DeferOutcome> TryDeferAsync(PremiumGrant grant, Subscription sub, CancellationToken ct)
    {
        var token = protector.Unprotect(sub.ProtectedPurchaseToken);
        if (token is null) return await NoteAsync(grant, "token_unreadable", ct);
        var duration = TimeSpan.FromDays(grant.Days);
        var slack = TimeSpan.FromMinutes(1);
        try
        {
            var info = await play.GetSubscriptionAsync(token, ct);
            if (info.SubscriptionState != PlaySubscription.Active || info.IsFreeTrial || !info.AutoRenewing || string.IsNullOrEmpty(info.Etag))
                return await NoteAsync(grant, "not_deferrable_now:" + Short(info.SubscriptionState), ct);

            // A previous attempt may have succeeded at Google without us seeing the answer: read the state back before deferring
            // again. A normal renewal in between changes the order id, so it is never mistaken for a deferral.
            if (grant.GoogleExpiryBefore is { } before && grant.GoogleOrderBefore == info.LatestOrderId && info.ExpiryTime >= before + duration - slack)
                return await ConfirmAsync(grant, sub, info.ExpiryTime, ct);
            if (grant.GoogleExpiryBefore is null || grant.GoogleOrderBefore != info.LatestOrderId)
            {
                grant.GoogleExpiryBefore = info.ExpiryTime;
                grant.GoogleOrderBefore = info.LatestOrderId;
            }
            grant.DeferAttempts++;
            grant.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct); // the intent is stored before calling Google

            await play.DeferAsync(token, info.Etag!, duration, validateOnly: true, ct);
            var result = await play.DeferAsync(token, info.Etag!, duration, validateOnly: false, ct);
            if (result.NewExpiry >= grant.GoogleExpiryBefore!.Value + duration - slack)
                return await ConfirmAsync(grant, sub, result.NewExpiry, ct);
            // Google answered but the date did not move as expected: do not claim success, check again next time.
            return await NoteAsync(grant, "defer_unconfirmed", ct);
        }
        catch (PlayBillingException ex)
        {
            logger.LogWarning(ex, "Deferring the renewal for reward {GrantId} failed ({Failure}); it stays pending.", grant.Id, ex.Failure);
            return await NoteAsync(grant, "defer_failed:" + ex.Failure + (ex.GoogleReason is null ? "" : " " + ex.GoogleReason), ct);
        }
    }

    private async Task<DeferOutcome> ConfirmAsync(PremiumGrant grant, Subscription sub, DateTimeOffset newExpiry, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        grant.Status = PremiumGrantStatuses.Deferred;
        grant.DeferredSubscriptionId = sub.Id;
        grant.GoogleExpiryAfter = newExpiry;
        grant.AppliedAt = now;
        grant.LastDeferError = null;
        grant.UpdatedAt = now;
        // Our cached copy follows what Google just confirmed; the next notification refreshes the rest of the row.
        if (newExpiry > sub.ExpiresAt) { sub.ExpiresAt = newExpiry; sub.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reward {GrantId} applied: Google deferred the renewal of subscription {SubscriptionId} to {Expiry}.", grant.Id, sub.Id, newExpiry);
        return DeferOutcome.Deferred;
    }

    private async Task<DeferOutcome> NoteAsync(PremiumGrant grant, string note, CancellationToken ct)
    {
        grant.LastDeferError = note.Length > 300 ? note[..300] : note;
        grant.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return DeferOutcome.StillPending;
    }

    private async Task<bool> ReferrerRenewsAsync(Guid referrerId, DateTimeOffset now, CancellationToken ct) =>
        (await db.Subscriptions.AsNoTracking().Where(s => s.UserId == referrerId).ToListAsync(ct))
        .Any(s => s.AutoRenewing && s.ExpiresAt > now && s.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod);

    private static string Short(string text) => text.Length > 40 ? text[..40] : text;
}
