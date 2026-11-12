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
