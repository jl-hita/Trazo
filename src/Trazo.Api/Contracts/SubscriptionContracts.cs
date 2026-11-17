namespace Trazo.Api.Contracts;

public sealed record VerifySubscriptionRequest(string? ProductId, string? PurchaseToken);

public sealed record AccessSettingsRequest(bool SubscriptionsEnforced);

public sealed record PromotionsSettingsRequest(bool SignupTrialEnabled, int SignupTrialDays, bool ReferralsEnabled, int ReferralRewardDays);

public sealed record PlayBillingSettingsRequest(
    string? PackageName, string? ServiceAccountJson, string? RtdnSecret, string? RtdnAudience, string? RtdnServiceAccountEmail);
