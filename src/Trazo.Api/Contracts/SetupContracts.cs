namespace Trazo.Api.Contracts;

public sealed record GoogleSettingsRequest(string? ClientId);
public sealed record EmailSettingsRequest(
    string? Host, int Port, bool EnableSsl, string? Username, string? Password, string? From, string? ActionBaseUrl);
public sealed record AdminAccountRequest(string? Email, string? DisplayName, string? Password);
public sealed record CompleteSetupRequest(GoogleSettingsRequest? Google, EmailSettingsRequest? Email, AdminAccountRequest? Admin);
public sealed record UpdateSettingsRequest(GoogleSettingsRequest? Google, EmailSettingsRequest? Email);
public sealed record TestGoogleRequest(string? ClientId);
public sealed record TestSmtpRequest(EmailSettingsRequest? Email, string? Recipient);
