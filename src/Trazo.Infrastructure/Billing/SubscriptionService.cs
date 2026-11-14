using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Billing;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Billing;

public enum VerifyOutcome { Verified, TokenInUse, AccountMismatch, ProductMismatch }

public sealed record VerifyResult(VerifyOutcome Outcome, Subscription? Subscription);

/// <summary>What the API tells a user about their own subscription.</summary>
public sealed record SubscriptionStatusView(
    bool HasAccess, string Status, string? ProductId, DateTimeOffset? StartedAt, DateTimeOffset? ExpiresAt,
    bool AutoRenewing, string Source);

public interface ISubscriptionService
{
    /// <summary>Validates a purchase with Google, links it to the user, acknowledges it and stores the result.</summary>
    Task<VerifyResult> VerifyAsync(Guid userId, string productId, string purchaseToken, CancellationToken ct);

    /// <summary>Re-reads a purchase from Google after a notification. Returns null when the token is not linked to any user.</summary>
    Task<Subscription?> SyncAsync(string purchaseToken, CancellationToken ct);

    /// <summary>Marks a purchase as refunded/voided. Returns null when the token is not linked to any user.</summary>
    Task<Subscription?> RevokeAsync(string purchaseToken, CancellationToken ct);

    /// <summary>Re-reads a stored subscription from Google (administration console).</summary>
    Task<Subscription?> RefreshAsync(Guid subscriptionId, CancellationToken ct);

    Task<SubscriptionStatusView> GetStatusAsync(Guid userId, CancellationToken ct);

    Task<bool> HasAccessAsync(Guid userId, CancellationToken ct);
}

public sealed class SubscriptionService(
    TrazoDbContext db, IPlayBillingClient play, ISecretProtector protector, TimeProvider clock,
    ILogger<SubscriptionService> logger) : ISubscriptionService
{
    public static string HashToken(string purchaseToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken)));

    public async Task<VerifyResult> VerifyAsync(Guid userId, string productId, string purchaseToken, CancellationToken ct)
    {
        var hash = HashToken(purchaseToken);
        var existing = await db.Subscriptions.SingleOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
        if (existing is not null && existing.UserId != userId)
            return new VerifyResult(VerifyOutcome.TokenInUse, null);

        var info = await play.GetSubscriptionAsync(purchaseToken, ct);
        if (!string.Equals(info.ProductId, productId, StringComparison.Ordinal))
            return new VerifyResult(VerifyOutcome.ProductMismatch, null);
        // The app sets the user's id as the obfuscated account id when it starts the purchase flow, so a
        // token bought by another account cannot be claimed here even before it is stored.
        if (!string.IsNullOrEmpty(info.ObfuscatedExternalAccountId) &&
            !string.Equals(info.ObfuscatedExternalAccountId, userId.ToString(), StringComparison.OrdinalIgnoreCase))
            return new VerifyResult(VerifyOutcome.AccountMismatch, null);

        var now = clock.GetUtcNow();
        var row = existing;
        if (row is null)
        {
            row = new Subscription
            {
                Id = Guid.NewGuid(), UserId = userId, ProductId = info.ProductId, PurchaseTokenHash = hash,
                ProtectedPurchaseToken = protector.Protect(purchaseToken), CreatedAt = now, UpdatedAt = now
            };
            db.Subscriptions.Add(row);
        }
        Apply(row, info, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests raced on the same purchase token: the unique index let exactly one through.
            db.ChangeTracker.Clear();
            var winner = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
            if (winner is null) throw;
            return winner.UserId == userId
                ? new VerifyResult(VerifyOutcome.Verified, winner)
                : new VerifyResult(VerifyOutcome.TokenInUse, null);
        }
        await AcknowledgeIfNeededAsync(row, info, purchaseToken, ct);
        return new VerifyResult(VerifyOutcome.Verified, row);
    }

    public async Task<Subscription?> SyncAsync(string purchaseToken, CancellationToken ct)
    {
        var hash = HashToken(purchaseToken);
        var row = await db.Subscriptions.SingleOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
        if (row is null) return null;
        return await SyncRowAsync(row, purchaseToken, ct);
    }

    public async Task<Subscription?> RefreshAsync(Guid subscriptionId, CancellationToken ct)
    {
        var row = await db.Subscriptions.SingleOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (row is null) return null;
        var token = protector.Unprotect(row.ProtectedPurchaseToken)
            ?? throw new InvalidOperationException("The stored purchase token cannot be decrypted (data-protection keys lost?).");
        return await SyncRowAsync(row, token, ct);
    }

    public async Task<Subscription?> RevokeAsync(string purchaseToken, CancellationToken ct)
    {
        var hash = HashToken(purchaseToken);
        var row = await db.Subscriptions.SingleOrDefaultAsync(x => x.PurchaseTokenHash == hash, ct);
        if (row is null) return null;
        row.Status = SubscriptionStatus.Revoked;
        row.AutoRenewing = false;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<SubscriptionStatusView> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Subscriptions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        // Prefer a row that grants access (latest expiry first), otherwise the most recent one.
        var best = rows.Where(x => SubscriptionAccess.Grants(x.Status, x.ExpiresAt, now))
                       .OrderByDescending(x => x.ExpiresAt).FirstOrDefault()
                   ?? rows.OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
        if (best is null)
            return new SubscriptionStatusView(false, "none", null, null, null, false, SubscriptionSources.GooglePlay);
        var access = SubscriptionAccess.Grants(best.Status, best.ExpiresAt, now);
        // A row whose period ended but was never refreshed is reported as expired.
        var status = !access && best.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active
            or SubscriptionStatus.GracePeriod or SubscriptionStatus.Canceled
            ? SubscriptionStatus.Expired : best.Status;
        return new SubscriptionStatusView(access, ToApiName(status), best.ProductId, best.StartedAt, best.ExpiresAt,
            best.AutoRenewing, best.Source);
    }

    public async Task<bool> HasAccessAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Subscriptions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        return rows.Any(x => SubscriptionAccess.Grants(x.Status, x.ExpiresAt, now));
    }

    public static string ToApiName(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.GracePeriod => "grace_period",
        SubscriptionStatus.OnHold => "on_hold",
        _ => status.ToString().ToLowerInvariant()
    };

    /// <summary>Maps Google's subscription state (and trial phase) to the status TRAZO stores.</summary>
    public static SubscriptionStatus MapStatus(PlaySubscription info, DateTimeOffset now) => info.SubscriptionState switch
    {
        PlaySubscription.Active => info.IsFreeTrial ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
        PlaySubscription.InGracePeriod => SubscriptionStatus.GracePeriod,
        PlaySubscription.OnHold or PlaySubscription.Paused => SubscriptionStatus.OnHold,
        PlaySubscription.Pending => SubscriptionStatus.Pending,
        // Cancelled subscriptions keep their access until the paid (or trial) period ends.
        PlaySubscription.Canceled => info.ExpiryTime > now ? SubscriptionStatus.Canceled : SubscriptionStatus.Expired,
        _ => SubscriptionStatus.Expired // EXPIRED, PENDING_PURCHASE_CANCELED and any unknown state: no access
    };

    private async Task<Subscription> SyncRowAsync(Subscription row, string purchaseToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        PlaySubscription info;
        try
        {
            info = await play.GetSubscriptionAsync(purchaseToken, ct);
        }
        catch (PlayBillingException ex) when (ex.Failure == PlayBillingFailure.InvalidPurchase)
        {
            // Google no longer knows the purchase (very old or removed): nothing grants access any more.
            if (row.Status != SubscriptionStatus.Revoked) row.Status = SubscriptionStatus.Expired;
            row.AutoRenewing = false;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return row;
        }
        Apply(row, info, now);
        await db.SaveChangesAsync(ct);
        await AcknowledgeIfNeededAsync(row, info, purchaseToken, ct);
        return row;
    }

    private static void Apply(Subscription row, PlaySubscription info, DateTimeOffset now)
    {
        var status = MapStatus(info, now);
        // A refund/void is final: only a brand-new period from Google (active again) lifts it.
        if (row.Status == SubscriptionStatus.Revoked &&
            status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial or SubscriptionStatus.GracePeriod))
            status = SubscriptionStatus.Revoked;
        row.Status = status;
        row.ProductId = info.ProductId;
        row.StoreState = info.SubscriptionState.Length > 60 ? info.SubscriptionState[..60] : info.SubscriptionState;
        row.LatestOrderId = info.LatestOrderId;
        row.StartedAt = info.StartTime;
        row.ExpiresAt = info.ExpiryTime;
        row.AutoRenewing = info.AutoRenewing;
        row.IsTestPurchase = info.IsTestPurchase;
        if (info.Acknowledged) row.AcknowledgedAt ??= now;
        row.UpdatedAt = now;
    }

    /// <summary>
    /// Google refunds purchases that are not acknowledged within three days, so this is retried on every
    /// notification when it fails. A failure never loses the verified purchase.
    /// </summary>
    private async Task AcknowledgeIfNeededAsync(Subscription row, PlaySubscription info, string purchaseToken, CancellationToken ct)
    {
        if (info.Acknowledged || row.AcknowledgedAt is not null) return;
        if (row.Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.GracePeriod or SubscriptionStatus.Canceled))
            return;
        try
        {
            await play.AcknowledgeAsync(info.ProductId, purchaseToken, ct);
            row.AcknowledgedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        catch (PlayBillingException ex)
        {
            logger.LogWarning(ex, "Could not acknowledge subscription {SubscriptionId}; it will be retried on the next notification.", row.Id);
        }
    }
}
