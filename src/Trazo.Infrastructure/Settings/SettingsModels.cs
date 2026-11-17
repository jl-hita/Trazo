namespace Trazo.Infrastructure.Settings;

public static class SettingKeys
{
    public const string GoogleClientId = "Authentication:Google:ClientId";
    public const string SmtpHost = "Email:Smtp:Host";
    public const string SmtpPort = "Email:Smtp:Port";
    public const string SmtpEnableSsl = "Email:Smtp:EnableSsl";
    public const string SmtpUsername = "Email:Smtp:Username";
    public const string SmtpPassword = "Email:Smtp:Password";
    public const string EmailFrom = "Email:From";
    public const string EmailActionBaseUrl = "Email:ActionBaseUrl";

    public const string SubscriptionsEnforced = "Billing:Enforced";
    public const string PlayPackageName = "Billing:Play:PackageName";
    public const string PlayServiceAccountJson = "Billing:Play:ServiceAccountJson";
    public const string PlayRtdnSecret = "Billing:Play:RtdnSecret";
    public const string PlayRtdnAudience = "Billing:Play:RtdnAudience";
    public const string PlayRtdnServiceAccountEmail = "Billing:Play:RtdnServiceAccountEmail";

    // Operational settings (never needed to start the API or to sign in; see docs/configuration.md).
    /// <summary>Extra CORS origins, one per line. They are added to Cors:AllowedOrigins from the environment, never replace it.</summary>
    public const string CorsAdditionalOrigins = "Cors:AdditionalOrigins";
    public const string RateLimitSetup = "RateLimits:Setup:PerMinute";
    public const string RateLimitSetupTest = "RateLimits:SetupTest:PerMinute";
    public const string RateLimitSubscription = "RateLimits:Subscription:PerMinute";
    public const string LogLevelDefault = "Logging:LogLevel:Default";
    public const string LogRetentionDays = "Logging:Database:RetentionDays";
    public const string DietoExpressEndpoint = "DietoExpressTransfer:Endpoint";
    public const string DietoExpressApiKey = "DietoExpressImport:ApiKey";

    // Push notifications (Firebase Cloud Messaging). The first four are what the Android app needs to start Firebase
    // (public by design, they ship inside every app); the service-account key signs the server's calls and is encrypted.
    public const string PushProjectId = "Push:Fcm:ProjectId";
    public const string PushApplicationId = "Push:Fcm:ApplicationId";
    public const string PushApiKey = "Push:Fcm:ApiKey";
    public const string PushSenderId = "Push:Fcm:SenderId";
    public const string PushServiceAccountJson = "Push:Fcm:ServiceAccountJson";

    // Promotions (superadmin): the welcome Premium period of new accounts and the referral programme.
    public const string SignupTrialEnabled = "Promotions:SignupTrial:Enabled";
    public const string SignupTrialDays = "Promotions:SignupTrial:Days";
    public const string ReferralsEnabled = "Promotions:Referrals:Enabled";
    public const string ReferralRewardDays = "Promotions:Referrals:RewardDays";

    /// <summary>Marker row: its primary key makes "complete the setup" a single-winner operation.</summary>
    public const string SetupCompletedAt = "Setup:CompletedAt";
}

/// <summary>
/// Promotional Premium time. Defaults: 30 days of welcome Premium for new accounts, and 30 days per paying referral, both on.
/// The days are recorded on each ledger row when granted, so changing them never rewrites the past.
/// </summary>
public sealed record PromotionsConfig(bool SignupTrialEnabled, int SignupTrialDays, bool ReferralsEnabled, int ReferralRewardDays)
{
    public const int DefaultDays = 30;
    public const int MaxDays = 365;
    public static readonly PromotionsConfig Default = new(true, DefaultDays, true, DefaultDays);

    public static Dictionary<string, string[]> Validate(PromotionsConfig input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.SignupTrialDays is < 1 or > MaxDays) errors["signupTrialDays"] = [$"Must be between 1 and {MaxDays} days."];
        if (input.ReferralRewardDays is < 1 or > MaxDays) errors["referralRewardDays"] = [$"Must be between 1 and {MaxDays} days."];
        return errors;
    }
}

public sealed record GoogleConfig(string ClientId);

public sealed record EmailConfig(
    string Host, int Port, bool EnableSsl, string? Username, string? Password, string From, string ActionBaseUrl)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From) &&
        !string.IsNullOrWhiteSpace(ActionBaseUrl) && Port is > 0 and <= 65535;
}

public sealed record AdminAccountInput(string Email, string DisplayName, string Password);

public sealed record SetupInput(GoogleConfig Google, EmailConfig Email, AdminAccountInput Admin);

/// <summary>Public view of the configuration state. Contains no secrets.</summary>
public sealed record SetupStatus(
    bool SetupRequired, bool NeedsSuperadmin, bool GoogleConfigured, bool EmailConfigured)
{
    public bool ConfigurationComplete => GoogleConfigured && EmailConfigured;
}

public enum SetupOutcome { Completed, AlreadyCompleted, Invalid }

public sealed record SetupResult(SetupOutcome Outcome, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Settings shown to a superadmin. The SMTP password is never included.</summary>
public sealed record SettingsView(
    string GoogleClientId, string SmtpHost, int SmtpPort, bool SmtpEnableSsl, string? SmtpUsername,
    bool SmtpPasswordSet, string EmailFrom, string EmailActionBaseUrl);

/// <summary>
/// Google Play Billing configuration. The service-account key and the notification secret are stored
/// encrypted. Real-time developer notifications (RTDN) are accepted with a Pub/Sub OIDC token
/// (RtdnAudience together with RtdnServiceAccountEmail, the account that signs the push) or the shared secret.
/// </summary>
public sealed record PlayBillingConfig(
    string PackageName, string? ServiceAccountJson, string? RtdnSecret, string? RtdnAudience, string? RtdnServiceAccountEmail)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(PackageName) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
    public bool RtdnAuthConfigured => !string.IsNullOrWhiteSpace(RtdnSecret) ||
        (!string.IsNullOrWhiteSpace(RtdnAudience) && !string.IsNullOrWhiteSpace(RtdnServiceAccountEmail));
}

/// <summary>
/// Effective operational settings after resolving database value, then environment/appsettings, then default.
/// <c>LogLevelOverride</c> is only set when a valid database value exists (it is what is layered over the environment).
/// </summary>
public sealed record OperationalConfig(
    IReadOnlyList<string> AdditionalCorsOrigins, int SetupPerMinute, int SetupTestPerMinute, int SubscriptionPerMinute,
    string LogLevel, string? LogLevelOverride, int LogRetentionDays)
{
    public const int DefaultSetupPerMinute = 30;
    public const int DefaultSetupTestPerMinute = 5;
    public const int DefaultSubscriptionPerMinute = 30;
    public const string DefaultLogLevel = "Information";
    public const int DefaultLogRetentionDays = 30;

    public static OperationalConfig Defaults { get; } = new(
        [], DefaultSetupPerMinute, DefaultSetupTestPerMinute, DefaultSubscriptionPerMinute,
        DefaultLogLevel, null, DefaultLogRetentionDays);
}

/// <summary>DietoExpress transfer destination. The API key is shared with the import endpoint and is stored encrypted.</summary>
public sealed record DietoExpressConfig(string Endpoint, string? ApiKey)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Superadmin input for the operational settings. A null <c>DietoExpressApiKey</c> keeps the stored key, empty clears it.</summary>
public sealed record OperationsInput(
    IReadOnlyList<string>? AdditionalCorsOrigins, int SetupPerMinute, int SetupTestPerMinute, int SubscriptionPerMinute,
    string? LogLevel, int LogRetentionDays, string? DietoExpressEndpoint, string? DietoExpressApiKey);

/// <summary>Shown to a superadmin. The DietoExpress key is never included, only where it comes from.</summary>
public sealed record OperationsView(
    IReadOnlyList<string> EnvironmentCorsOrigins, IReadOnlyList<string> AdditionalCorsOrigins,
    int SetupPerMinute, int SetupTestPerMinute, int SubscriptionPerMinute, string LogLevel, int LogRetentionDays,
    string DietoExpressEndpoint, bool DietoExpressApiKeySet, string DietoExpressApiKeySource);

/// <summary>What changed in an update, for the audit log. Secret values are never part of it.</summary>
public sealed record OperationsUpdateResult(
    IReadOnlyDictionary<string, string[]> Errors, IReadOnlyList<string> Changes)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>Superadmin input: null secrets keep the stored value, empty strings clear it.</summary>
public sealed record PlayBillingInput(
    string PackageName, string? ServiceAccountJson, string? RtdnSecret, string? RtdnAudience, string? RtdnServiceAccountEmail);

/// <summary>Shown to a superadmin. Secrets are never included.</summary>
public sealed record PlayBillingView(
    string PackageName, bool ServiceAccountSet, string? ServiceAccountEmail, bool RtdnSecretSet,
    string? RtdnAudience, string? RtdnServiceAccountEmail, bool Configured, bool RtdnConfigured);

/// <summary>
/// Firebase Cloud Messaging configuration. <c>ProjectId</c>, <c>ApplicationId</c>, <c>ApiKey</c> and <c>SenderId</c> are the
/// client options the Android app uses to start Firebase (it asks the API for them, so no google-services.json is built into
/// the app); <c>ServiceAccountJson</c> is the server key used to send messages and is stored encrypted.
/// </summary>
public sealed record PushConfig(
    string ProjectId, string ApplicationId, string ApiKey, string? SenderId, string? ServiceAccountJson)
{
    /// <summary>The app can start Firebase and register its token.</summary>
    public bool ClientConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApplicationId) && !string.IsNullOrWhiteSpace(ApiKey);
    /// <summary>The server can also send.</summary>
    public bool IsComplete => ClientConfigured && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}

/// <summary>Superadmin input: a null <c>ServiceAccountJson</c> keeps the stored key, empty clears it.</summary>
public sealed record PushInput(string ProjectId, string ApplicationId, string ApiKey, string? SenderId, string? ServiceAccountJson);

/// <summary>Shown to a superadmin. The service-account key is never included, only whom it belongs to.</summary>
public sealed record PushView(
    string ProjectId, string ApplicationId, string ApiKey, string? SenderId, bool ServiceAccountSet,
    string? ServiceAccountEmail, bool Configured, int DeviceCount);

/// <summary>What the signed-in app needs to start Firebase. Nothing in it is secret.</summary>
public sealed record PushClientConfig(bool Enabled, string? ProjectId, string? ApplicationId, string? ApiKey, string? SenderId);
