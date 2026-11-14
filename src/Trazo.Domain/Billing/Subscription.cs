namespace Trazo.Domain.Billing;

/// <summary>Lifecycle of a store subscription, normalised from the store's own states.</summary>
public enum SubscriptionStatus
{
    /// <summary>The payment is still being processed by the store; no access yet.</summary>
    Pending,
    /// <summary>Free trial in progress.</summary>
    Trial,
    Active,
    /// <summary>A renewal payment failed but the store still gives the user access while it retries.</summary>
    GracePeriod,
    /// <summary>Renewal failed and the grace period ended (or the subscription is paused): no access.</summary>
    OnHold,
    /// <summary>The user cancelled auto-renewal; access continues until <see cref="Subscription.ExpiresAt"/>.</summary>
    Canceled,
    Expired,
    /// <summary>Refunded or voided: access is withdrawn immediately.</summary>
    Revoked
}

public static class SubscriptionSources
{
    public const string GooglePlay = "google_play";
}

/// <summary>A user's subscription purchase. The store is the source of truth; this row is a verified cache of it.</summary>
public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Source { get; set; } = SubscriptionSources.GooglePlay;
    public required string ProductId { get; set; }
    /// <summary>SHA-256 (hex) of the purchase token: the unique key that stops one purchase being linked to two users.</summary>
    public required string PurchaseTokenHash { get; set; }
    /// <summary>The purchase token encrypted with Data Protection; needed to query the store again.</summary>
    public required string ProtectedPurchaseToken { get; set; }
    public SubscriptionStatus Status { get; set; }
    /// <summary>The store's own state name, kept for diagnostics (e.g. SUBSCRIPTION_STATE_ACTIVE).</summary>
    public string? StoreState { get; set; }
    public string? LatestOrderId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool AutoRenewing { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public bool IsTestPurchase { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The single place that decides whether a subscription row gives access to paid content.</summary>
public static class SubscriptionAccess
{
    public static bool Grants(SubscriptionStatus status, DateTimeOffset expiresAt, DateTimeOffset now) =>
        expiresAt > now && status is SubscriptionStatus.Trial or SubscriptionStatus.Active
            or SubscriptionStatus.GracePeriod or SubscriptionStatus.Canceled;
}
