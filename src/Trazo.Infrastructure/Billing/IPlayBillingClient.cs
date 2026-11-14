namespace Trazo.Infrastructure.Billing;

/// <summary>A subscription purchase as reported by Google Play (subscriptionsv2.get), reduced to what TRAZO needs.</summary>
public sealed record PlaySubscription(
    string SubscriptionState,
    string ProductId,
    DateTimeOffset StartTime,
    DateTimeOffset ExpiryTime,
    bool AutoRenewing,
    bool IsFreeTrial,
    bool Acknowledged,
    string? LatestOrderId,
    string? ObfuscatedExternalAccountId,
    bool IsTestPurchase)
{
    public const string Active = "SUBSCRIPTION_STATE_ACTIVE";
    public const string Pending = "SUBSCRIPTION_STATE_PENDING";
    public const string Paused = "SUBSCRIPTION_STATE_PAUSED";
    public const string InGracePeriod = "SUBSCRIPTION_STATE_IN_GRACE_PERIOD";
    public const string OnHold = "SUBSCRIPTION_STATE_ON_HOLD";
    public const string Canceled = "SUBSCRIPTION_STATE_CANCELED";
    public const string Expired = "SUBSCRIPTION_STATE_EXPIRED";
    public const string PendingPurchaseCanceled = "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED";
}

public enum PlayBillingFailure
{
    /// <summary>Package name or service account has not been configured in the administration console.</summary>
    NotConfigured,
    /// <summary>Google does not know this purchase token (or it does not belong to this app).</summary>
    InvalidPurchase,
    /// <summary>Google rejected the service account (not linked in Play Console, no permission, bad key).</summary>
    Unauthorized,
    /// <summary>Google could not be reached or answered with a server error: retry later.</summary>
    Unavailable
}

public sealed class PlayBillingException(PlayBillingFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public PlayBillingFailure Failure { get; } = failure;
}

/// <summary>
/// Google Play Developer API operations used for subscriptions. Credentials and the package name
/// come from the settings saved in the administration console, never from the client app.
/// </summary>
public interface IPlayBillingClient
{
    Task<PlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken ct);

    Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct);

    /// <summary>
    /// Checks that the stored credentials and package name are accepted by Google, without needing a real purchase.
    /// Throws <see cref="PlayBillingException"/> when they are not.
    /// </summary>
    Task CheckConnectionAsync(CancellationToken ct);
}
