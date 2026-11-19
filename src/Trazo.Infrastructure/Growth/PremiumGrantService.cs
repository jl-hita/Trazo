using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Growth;

public sealed record GrantRequest(
    Guid UserId, string Kind, string SourceKey, int Days, Guid? OriginUserId = null, Guid? OriginSubscriptionId = null,
    string Status = PremiumGrantStatuses.Active, bool IsPermanent = false, string? Reason = null);

public sealed record GrantResult(PremiumGrant Grant, bool Created);

public enum VoidOutcome { Voided, AlreadyVoided, NotFound, NotReversible }

public interface IPremiumGrantService
{
    /// <summary>
    /// Records a grant. Idempotent: the (kind, source key) pair is unique, so a second call for the same reason, or two
    /// racing calls, return the existing row (<see cref="GrantResult.Created"/> false) and never a second grant.
    /// Active grants are chained after the user's earlier promotional time (and after a non-renewing Google Play period).
    /// </summary>
    Task<GrantResult> GrantAsync(GrantRequest request, CancellationToken ct);

    /// <summary>True when the user is inside an active promotional window right now.</summary>
    Task<bool> HasPromoAsync(Guid userId, CancellationToken ct);

    /// <summary>End of the user's current promotional access, or null when there is none now.</summary>
    Task<DateTimeOffset?> PromoEndAsync(Guid userId, CancellationToken ct);

    /// <summary>Cancels a grant and closes the gap it leaves in the user's chain. A deferral already applied by Google cannot be undone.</summary>
    Task<VoidOutcome> VoidAsync(Guid grantId, string reason, string by, CancellationToken ct);

    /// <summary>Turns a pending grant into an active promotional window (used when it can no longer be applied as a deferral).</summary>
    Task<PremiumGrant?> ActivatePendingAsync(Guid grantId, CancellationToken ct);

    /// <summary>Where the next active window of this user would start.</summary>
    Task<DateTimeOffset> NextStartAsync(Guid userId, CancellationToken ct);
}

public sealed class PremiumGrantService(TrazoDbContext db, TimeProvider clock, ILogger<PremiumGrantService> logger) : IPremiumGrantService
{
    public async Task<GrantResult> GrantAsync(GrantRequest request, CancellationToken ct)
    {
        if (request.IsPermanent && request.Kind != PremiumGrantKinds.AdminGrant)
            throw new ArgumentException("Only manual administrative grants can be permanent.", nameof(request));
        if ((!request.IsPermanent && request.Days <= 0) || (request.IsPermanent && request.Days != 0))
            throw new ArgumentOutOfRangeException(nameof(request), "A finite grant needs positive days; a permanent grant uses zero days.");
        if (request.Kind == PremiumGrantKinds.AdminGrant && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A manual administrative grant needs a reason.", nameof(request));
        var existing = await db.PremiumGrants.SingleOrDefaultAsync(x => x.Kind == request.Kind && x.SourceKey == request.SourceKey, ct);
        if (existing is not null) return new GrantResult(existing, false);

        var now = clock.GetUtcNow();
        var grant = new PremiumGrant
        {
            Id = Guid.NewGuid(), UserId = request.UserId, Kind = request.Kind, SourceKey = request.SourceKey, Days = request.Days,
            Status = request.Status, GrantedAt = now, OriginUserId = request.OriginUserId, IsPermanent = request.IsPermanent, GrantReason = request.Reason?.Trim(),
            OriginSubscriptionId = request.OriginSubscriptionId, UpdatedAt = now
        };
        if (request.Status == PremiumGrantStatuses.Active) Place(grant, await NextStartAsync(request.UserId, ct), now);
        db.PremiumGrants.Add(grant);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request recorded the same reason first: the unique index let exactly one through.
            db.Entry(grant).State = EntityState.Detached;
            var winner = await db.PremiumGrants.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == request.Kind && x.SourceKey == request.SourceKey, ct);
            if (winner is null) throw;
            return new GrantResult(winner, false);
        }
        logger.LogInformation("Premium grant {Kind} of {Days} days recorded for user {UserId} ({Status}).", grant.Kind, grant.Days, grant.UserId, grant.Status);
        return new GrantResult(grant, true);
    }

    public async Task<DateTimeOffset> NextStartAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var start = now;
        var ends = (await db.PremiumGrants.AsNoTracking().Where(x => x.UserId == userId && x.Status == PremiumGrantStatuses.Active).ToListAsync(ct))
            .Select(x => x.EndsAt).Where(x => x is not null).Select(x => x!.Value);
        foreach (var end in ends) if (end > start) start = end;
        // A subscription that will not renew keeps its paid period: the promotional time starts when it ends.
        var subs = await db.Subscriptions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var s in subs.Where(s => !s.AutoRenewing && SubscriptionAccess.Grants(s.Status, s.ExpiresAt, now)))
            if (s.ExpiresAt > start) start = s.ExpiresAt;
        return start;
    }

    public async Task<bool> HasPromoAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.PremiumGrants.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.Status == PremiumGrantStatuses.Active && x.StartsAt <= now &&
            (x.IsPermanent || x.EndsAt > now), ct);
    }

    public async Task<DateTimeOffset?> PromoEndAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.PremiumGrants.AsNoTracking().Where(x => x.UserId == userId && x.Status == PremiumGrantStatuses.Active).ToListAsync(ct);
        // A permanent manual grant has no end date. HasPromoAsync still reports access; null here means no finite expiry.
        if (rows.Any(x => x.IsPermanent && x.StartsAt <= now)) return null;
        var current = rows.Where(x => !x.IsPermanent && x.StartsAt <= now && x.EndsAt > now).Select(x => x.EndsAt).ToList();
        if (current.Count == 0) return null;
        // Chained windows: access lasts until the last window that is contiguous with the current one.
        var end = current.Max()!.Value;
        foreach (var next in rows.Where(x => x.StartsAt > now).OrderBy(x => x.StartsAt))
        {
            if (next.StartsAt <= end) { if (next.EndsAt > end) end = next.EndsAt!.Value; }
            else break;
        }
        return end;
    }

    public async Task<PremiumGrant?> ActivatePendingAsync(Guid grantId, CancellationToken ct)
    {
        var grant = await db.PremiumGrants.SingleOrDefaultAsync(x => x.Id == grantId, ct);
        if (grant is null || grant.Status != PremiumGrantStatuses.Pending) return grant;
        var now = clock.GetUtcNow();
        grant.Status = PremiumGrantStatuses.Active;
        Place(grant, await NextStartAsync(grant.UserId, ct), now);
        grant.AppliedAt = now;
        await db.SaveChangesAsync(ct);
        return grant;
    }

    public async Task<VoidOutcome> VoidAsync(Guid grantId, string reason, string by, CancellationToken ct)
    {
        var grant = await db.PremiumGrants.SingleOrDefaultAsync(x => x.Id == grantId, ct);
        if (grant is null) return VoidOutcome.NotFound;
        if (grant.Status == PremiumGrantStatuses.Voided) return VoidOutcome.AlreadyVoided;
        // Google gives no way to take a deferral back, so the days already applied to the renewal date stay.
        if (grant.Status == PremiumGrantStatuses.Deferred) return VoidOutcome.NotReversible;

        var now = clock.GetUtcNow();
        var wasActive = grant.Status == PremiumGrantStatuses.Active;
        var cursor = grant.StartsAt is { } s && s > now ? s : now;
        grant.Status = PremiumGrantStatuses.Voided;
        grant.VoidedAt = now;
        grant.VoidReason = reason.Length > 300 ? reason[..300] : reason;
        grant.VoidedBy = by.Length > 40 ? by[..40] : by;
        grant.UpdatedAt = now;
        if (wasActive)
        {
            // Close the gap: later windows of the same user move forward in the chain.
            var activeRows = await db.PremiumGrants.Where(x => x.UserId == grant.UserId &&
                x.Status == PremiumGrantStatuses.Active && x.Id != grant.Id).ToListAsync(ct);
            var later = (grant.IsPermanent
                ? activeRows.Where(x => !x.IsPermanent && x.StartsAt >= grant.StartsAt)
                : activeRows.Where(x => x.StartsAt >= grant.StartsAt))
                .OrderBy(x => x.StartsAt).ToList();
            foreach (var next in later)
            {
                next.StartsAt = cursor;
                next.EndsAt = next.IsPermanent ? null : cursor.AddDays(next.Days);
                next.UpdatedAt = now;
                if (next.EndsAt is { } end) cursor = end;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Premium grant {GrantId} voided by {By}: {Reason}", grant.Id, by, reason);
        return VoidOutcome.Voided;
    }

    private static void Place(PremiumGrant grant, DateTimeOffset start, DateTimeOffset now)
    {
        grant.StartsAt = start;
        grant.EndsAt = grant.IsPermanent ? null : start.AddDays(grant.Days);
        grant.AppliedAt ??= now;
    }
}
