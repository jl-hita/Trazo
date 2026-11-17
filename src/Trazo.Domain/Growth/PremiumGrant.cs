namespace Trazo.Domain.Growth;

/// <summary>Why a user was given Premium time outside Google Play.</summary>
public static class PremiumGrantKinds
{
    /// <summary>Welcome period given to a new account (length and on/off are superadmin settings).</summary>
    public const string SignupTrial = "signup_trial";
    /// <summary>Reward for the referrer when a referred user pays their first subscription.</summary>
    public const string ReferralReward = "referral_reward";
    /// <summary>Manual adjustment by a superadmin; always carries a written reason.</summary>
    public const string AdminGrant = "admin_grant";
}

public static class PremiumGrantStatuses
{
    /// <summary>A promotional window [StartsAt, EndsAt) that gives Premium access by itself.</summary>
    public const string Active = "active";
    /// <summary>
    /// Earned but not applied yet: the user has an auto-renewing Google Play subscription and the renewal could not be
    /// deferred (yet). It is applied as a deferral when Google confirms one, or as a promotional window once the
    /// subscription stops renewing. Gives no access by itself, and is never lost.
    /// </summary>
    public const string Pending = "pending";
    /// <summary>Applied by deferring the Google Play renewal; Google confirmed the new expiry (see <see cref="PremiumGrant.GoogleExpiryAfter"/>).</summary>
    public const string Deferred = "deferred";
    /// <summary>Cancelled (for example because the first payment was refunded). Kept for the audit trail.</summary>
    public const string Voided = "voided";
}

/// <summary>
/// One movement of the Premium-time ledger. It is independent from <c>subscriptions</c> (the Google Play purchases, which
/// are never altered to fake a purchase): effective access is the union of the store subscription and the active grants.
/// Rows are never deleted; they are only applied, voided or re-placed, and each change is logged.
/// </summary>
public sealed class PremiumGrant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Kind { get; set; }
    /// <summary>Natural key of the reason (the referral, the account...). (Kind, SourceKey) is unique, so a reason can never be rewarded twice.</summary>
    public required string SourceKey { get; set; }
    public int Days { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    /// <summary>Effective window of an active grant. Grants of one user are chained, never overlapped.</summary>
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    /// <summary>The user whose purchase produced this grant (the referred user), when there is one.</summary>
    public Guid? OriginUserId { get; set; }
    /// <summary>The store subscription whose first payment produced this grant.</summary>
    public Guid? OriginSubscriptionId { get; set; }
    /// <summary>For deferrals: the subscription that was deferred and the expiry Google reported before and after.</summary>
    public Guid? DeferredSubscriptionId { get; set; }
    public DateTimeOffset? GoogleExpiryBefore { get; set; }
    public DateTimeOffset? GoogleExpiryAfter { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    /// <summary>Who voided it: a user id, or "system".</summary>
    public string? VoidedBy { get; set; }
    public int DeferAttempts { get; set; }
    public string? LastDeferError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
