namespace Trazo.Domain.Growth;

/// <summary>
/// Catalogue (version 1) of the events that describe the path from acquisition to payment and retention. The same table
/// serves the general conversion funnel and the referral funnel: referral events only add a <see cref="GrowthEvent.Reference"/>.
/// Economic events are written by the backend from purchases verified with Google Play, never declared by the app.
/// </summary>
public static class GrowthEventNames
{
    public const string Signup = "signup";
    public const string EmailVerified = "email_verified";
    public const string OnboardingCompleted = "onboarding_completed";
    public const string FirstPlan = "first_plan";
    public const string FirstWorkout = "first_workout";
    public const string TrialStarted = "trial_started";
    public const string FirstPurchase = "first_purchase";
    public const string Renewed = "renewed";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string Refunded = "refunded";
    public const string SignupTrialGranted = "signup_trial_granted";

    /// <summary>The account arrived through a marketing campaign link (see Trazo.Domain.Marketing). Reference is the campaign slug.</summary>
    public const string CampaignAttributed = "campaign_attributed";

    public const string ReferralCodeCreated = "referral_code_created";
    public const string ReferralShared = "referral_shared";
    public const string ReferralLinkOpened = "referral_link_opened";
    public const string ReferralInstallAttributed = "referral_install_attributed";
    public const string ReferralAttributed = "referral_attributed";
    public const string ReferralRewardGranted = "referral_reward_granted";
    public const string ReferralRewardVoided = "referral_reward_voided";

    /// <summary>Events the app is allowed to report (they describe the user's own app usage, never money).</summary>
    public static readonly string[] ClientReportable = [ReferralShared, OnboardingCompleted];
}

/// <summary>
/// One occurrence of a funnel event. Privacy by design: it holds no e-mail, name, IP, device id or free text. The user id is
/// nullable and set to null when the account is deleted, which leaves an anonymous row that still counts in aggregates.
/// </summary>
public sealed class GrowthEvent
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Where the user came from when known: "referral", "organic"...</summary>
    public string? Channel { get; set; }
    /// <summary>Referral code or campaign the event belongs to.</summary>
    public string? Reference { get; set; }
    /// <summary>Subscription product (monthly or annual) for purchase events.</summary>
    public string? Plan { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Experiment arm, reserved for future experiments (invitation message, trial length, onboarding variant...).</summary>
    public string? Variant { get; set; }
    /// <summary>Makes recording idempotent: a second event with the same key is ignored (duplicate notifications, retries).</summary>
    public string? DedupeKey { get; set; }
}
