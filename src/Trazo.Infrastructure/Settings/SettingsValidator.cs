using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace Trazo.Infrastructure.Settings;

public static class SettingsValidator
{
    public const string GoogleClientIdSuffix = ".apps.googleusercontent.com";

    public static bool IsValidPassword(string? password) =>
        password is { Length: >= 12 and <= 128 } &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) || email.Length > 320 || !new EmailAddressAttribute().IsValid(email.Trim())
            ? null : email.Trim().ToUpperInvariant();

    public static Dictionary<string, string[]> ValidateGoogle(GoogleConfig? google)
    {
        var errors = new Dictionary<string, string[]>();
        var id = google?.ClientId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 256 || !id.EndsWith(GoogleClientIdSuffix, StringComparison.Ordinal) ||
            id.Length == GoogleClientIdSuffix.Length || id.Any(char.IsWhiteSpace))
            errors["google.clientId"] = [$"Expected a Web client ID ending in {GoogleClientIdSuffix}."];
        return errors;
    }

    public static Dictionary<string, string[]> ValidateEmail(EmailConfig? email)
    {
        var errors = new Dictionary<string, string[]>();
        if (email is null) { errors["email"] = ["Required."]; return errors; }
        if (string.IsNullOrWhiteSpace(email.Host) || email.Host.Trim().Length > 255 || email.Host.Trim().Any(char.IsWhiteSpace))
            errors["email.host"] = ["A valid SMTP host is required."];
        if (email.Port is < 1 or > 65535) errors["email.port"] = ["Port must be between 1 and 65535."];
        if (string.IsNullOrWhiteSpace(email.From) || email.From.Length > 320 || !MailAddress.TryCreate(email.From.Trim(), out _))
            errors["email.from"] = ["A valid sender address is required."];
        if (string.IsNullOrWhiteSpace(email.ActionBaseUrl) || email.ActionBaseUrl.Length > 500 ||
            !Uri.TryCreate(email.ActionBaseUrl.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Scheme))
            errors["email.actionBaseUrl"] = ["An absolute URL (https://... or an app scheme such as trazo://account) is required."];
        if (email.Username is { Length: > 320 }) errors["email.username"] = ["Too long."];
        if (email.Password is { Length: > 1000 }) errors["email.password"] = ["Too long."];
        return errors;
    }

    public const int MaxServiceAccountJsonLength = 8000;

    /// <summary>The client_email of a Google service-account key, or null when the JSON is not one.</summary>
    public static string? ServiceAccountEmail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.GetString() != "service_account" ||
                !root.TryGetProperty("client_email", out var email) || email.ValueKind != System.Text.Json.JsonValueKind.String ||
                !root.TryGetProperty("private_key", out var key) || string.IsNullOrWhiteSpace(key.GetString()))
                return null;
            return email.GetString();
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public static Dictionary<string, string[]> ValidatePlayBilling(PlayBillingInput? input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input is null) { errors["playBilling"] = ["Required."]; return errors; }
        var package = input.PackageName?.Trim();
        // Android application IDs: dot-separated segments of letters, digits and underscores.
        if (string.IsNullOrEmpty(package) || package.Length > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(package, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$"))
            errors["packageName"] = ["Expected an Android package name such as com.trazo.android."];
        if (input.ServiceAccountJson is { Length: > 0 } json &&
            (json.Length > MaxServiceAccountJsonLength || ServiceAccountEmail(json) is null))
            errors["serviceAccountJson"] = ["Paste the full JSON key of a Google service account."];
        if (input.RtdnSecret is { Length: > 200 }) errors["rtdnSecret"] = ["Too long."];
        if (input.RtdnSecret is { Length: > 0 and < 16 }) errors["rtdnSecret"] = ["Use at least 16 characters."];
        if (input.RtdnAudience is { Length: > 500 }) errors["rtdnAudience"] = ["Too long."];
        if (input.RtdnServiceAccountEmail is { Length: > 0 } pusher &&
            (pusher.Length > 320 || !new EmailAddressAttribute().IsValid(pusher.Trim())))
            errors["rtdnServiceAccountEmail"] = ["A valid service-account email is required."];
        return errors;
    }

    public static Dictionary<string, string[]> ValidateAdmin(AdminAccountInput? admin)
    {
        var errors = new Dictionary<string, string[]>();
        if (admin is null) { errors["admin"] = ["Required."]; return errors; }
        if (NormalizeEmail(admin.Email) is null) errors["admin.email"] = ["A valid email is required."];
        if (admin.DisplayName is null || admin.DisplayName.Trim().Length is < 2 or > 100)
            errors["admin.displayName"] = ["Display name must have 2-100 characters."];
        if (!IsValidPassword(admin.Password))
            errors["admin.password"] = ["Use at least 12 characters including uppercase, lowercase and a number."];
        return errors;
    }
}
