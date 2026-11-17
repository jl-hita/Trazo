namespace Trazo.Domain.Marketing;

/// <summary>Kinds of acquisition channel a campaign belongs to. "organic" and "referral" are not campaigns: they are the baselines.</summary>
public static class CampaignChannels
{
    public const string Organic = "organic";
    public const string Referral = "referral";
    public static readonly string[] All = ["social", "influencer", "ads", "email", "partner", "content", "press", "event", "other"];
}

public static class CampaignStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Archived = "archived";
    public static readonly string[] All = [Active, Paused, Archived];
}

public static class CampaignSources
{
    /// <summary>Delivered by the Google Play Install Referrer on the first launch after installing from the campaign link.</summary>
    public const string InstallReferrer = "install_referrer";
    public const string Link = "link";
    public static readonly string[] All = [InstallReferrer, Link];
    public static string Normalize(string? value) => All.Contains(value) ? value! : InstallReferrer;
}

/// <summary>
/// A marketing effort with its own tracking link (<c>/c/{slug}</c>). The slug never changes after creation because the link
/// is already out in the world. A campaign holds no personal data.
/// </summary>
public sealed class MarketingCampaign
{
    public Guid Id { get; set; }
    /// <summary>Lower-case letters, digits and dashes (3 to 32 characters); unique.</summary>
    public required string Slug { get; set; }
    public required string Name { get; set; }
    /// <summary>One of <see cref="CampaignChannels.All"/>.</summary>
    public required string Channel { get; set; }
    public required string Status { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    /// <summary>What was done and why (creative, audience, partner, offer). Free text of the marketer, never about users.</summary>
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The campaign a new account came from. Created once, when the account is created, from the code the app read from the Play
/// Install Referrer; it is never attached to an existing account. Deleted together with the account.
/// </summary>
public sealed class CampaignAttribution
{
    public Guid UserId { get; set; }
    public Guid CampaignId { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset AttributedAt { get; set; }
}

/// <summary>Anonymous per-day counters of a campaign link: people who opened it and installs the app could attribute to it.</summary>
public sealed class CampaignDailyStat
{
    public Guid CampaignId { get; set; }
    public DateOnly Day { get; set; }
    public int Clicks { get; set; }
    public int Installs { get; set; }
}

/// <summary>Money spent on a campaign on a given day (ads, a fee, a gift...). Entered by the marketer, in euros.</summary>
public sealed class CampaignSpend
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public DateOnly Day { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
