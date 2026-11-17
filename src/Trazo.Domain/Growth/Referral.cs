namespace Trazo.Domain.Growth;

/// <summary>A user's personal invitation code: one per user, permanent, unique.</summary>
public sealed class ReferralCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Upper-case, from an alphabet without look-alike characters (no 0/O, 1/I/L).</summary>
    public required string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class ReferralStatuses
{
    /// <summary>The invited person signed up; they have not paid yet.</summary>
    public const string Registered = "registered";
    /// <summary>First real payment confirmed by Google Play; the reward was recorded.</summary>
    public const string Rewarded = "rewarded";
    /// <summary>The first payment was confirmed while the programme was off: no reward.</summary>
    public const string Skipped = "skipped";
    /// <summary>The reward was cancelled (the first payment was refunded or revoked).</summary>
    public const string Voided = "voided";
}

public static class ReferralSources
{
    public const string Link = "link";
    public const string Manual = "manual";
    public const string InstallReferrer = "install_referrer";
    public static readonly string[] All = [Link, Manual, InstallReferrer];
    public static string Normalize(string? value) => All.Contains(value) ? value! : Manual;
}

/// <summary>
/// Who invited whom. Created once, when the invited account is created, and never changed to another referrer
/// (<c>ReferredUserId</c> is unique). Accounts that existed before cannot get one: nothing attaches a code afterwards.
/// </summary>
public sealed class ReferralRelationship
{
    public Guid Id { get; set; }
    public Guid ReferrerUserId { get; set; }
    public Guid ReferredUserId { get; set; }
    public required string Code { get; set; }
    public required string Source { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset AttributedAt { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>The store purchase whose first real payment converted this invitation.</summary>
    public Guid? ConversionSubscriptionId { get; set; }
    public string? ConversionPlan { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public Guid? RewardGrantId { get; set; }
    public int? RewardDays { get; set; }
    /// <summary>Something an administrator should look at, e.g. "reward_not_reversible" or "defer_failing".</summary>
    public string? Incident { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
