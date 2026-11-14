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

    public const string PlayPackageName = "Billing:Play:PackageName";
    public const string PlayServiceAccountJson = "Billing:Play:ServiceAccountJson";
    public const string PlayRtdnSecret = "Billing:Play:RtdnSecret";
    public const string PlayRtdnAudience = "Billing:Play:RtdnAudience";
    public const string PlayRtdnServiceAccountEmail = "Billing:Play:RtdnServiceAccountEmail";

    /// <summary>Marker row: its primary key makes "complete the setup" a single-winner operation.</summary>
    public const string SetupCompletedAt = "Setup:CompletedAt";
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
/// (RtdnAudience, optionally pinned to RtdnServiceAccountEmail) or the shared secret.
/// </summary>
public sealed record PlayBillingConfig(
    string PackageName, string? ServiceAccountJson, string? RtdnSecret, string? RtdnAudience, string? RtdnServiceAccountEmail)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(PackageName) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
    public bool RtdnAuthConfigured => !string.IsNullOrWhiteSpace(RtdnSecret) || !string.IsNullOrWhiteSpace(RtdnAudience);
}

/// <summary>Superadmin input: null secrets keep the stored value, empty strings clear it.</summary>
public sealed record PlayBillingInput(
    string PackageName, string? ServiceAccountJson, string? RtdnSecret, string? RtdnAudience, string? RtdnServiceAccountEmail);

/// <summary>Shown to a superadmin. Secrets are never included.</summary>
public sealed record PlayBillingView(
    string PackageName, bool ServiceAccountSet, string? ServiceAccountEmail, bool RtdnSecretSet,
    string? RtdnAudience, string? RtdnServiceAccountEmail, bool Configured, bool RtdnConfigured);
