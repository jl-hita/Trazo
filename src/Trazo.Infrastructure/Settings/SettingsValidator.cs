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
            !Uri.TryCreate(email.ActionBaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            errors["email.actionBaseUrl"] = ["An absolute https:// URL is required (http only for localhost). App schemes such as trazo:// are not accepted: any app can claim them."];
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
        // The audience alone proves nothing: any Google service account can get a token for it.
        if (!string.IsNullOrWhiteSpace(input.RtdnAudience) && string.IsNullOrWhiteSpace(input.RtdnServiceAccountEmail))
            errors["rtdnServiceAccountEmail"] = ["Required with the audience: the service account that signs the Pub/Sub push."];
        return errors;
    }

    /// <summary>The project_id of a Google service-account key, or null.</summary>
    public static string? ServiceAccountProjectId(string? json)
    {
        if (ServiceAccountEmail(json) is null) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json!);
            return doc.RootElement.TryGetProperty("project_id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String
                ? id.GetString() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static readonly System.Text.RegularExpressions.Regex FirebaseProjectId = new("^[a-z][a-z0-9-]{4,28}[a-z0-9]$");
    private static readonly System.Text.RegularExpressions.Regex FirebaseAppId = new(@"^\d{1,3}:\d{6,20}:android:[0-9a-f]{8,64}$");
    private static readonly System.Text.RegularExpressions.Regex FirebaseApiKey = new(@"^[A-Za-z0-9_\-]{20,100}$");

    /// <summary>
    /// All three client values empty (and no key) switches push off; otherwise they are all required. When a key is
    /// supplied it must belong to the same project. <paramref name="storedServiceAccountJson"/> is checked the same way when no new key is sent.
    /// </summary>
    public static Dictionary<string, string[]> ValidatePush(PushInput? input, string? storedServiceAccountJson = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (input is null) { errors["push"] = ["Required."]; return errors; }
        var project = input.ProjectId?.Trim() ?? "";
        var app = input.ApplicationId?.Trim() ?? "";
        var apiKey = input.ApiKey?.Trim() ?? "";
        var sender = input.SenderId?.Trim() ?? "";
        var off = project.Length == 0 && app.Length == 0 && apiKey.Length == 0 && input.ServiceAccountJson is not { Length: > 0 };
        if (off) return errors;

        if (!FirebaseProjectId.IsMatch(project)) errors["projectId"] = ["Expected the Firebase project ID (lower case letters, digits and hyphens)."];
        if (!FirebaseAppId.IsMatch(app)) errors["applicationId"] = ["Expected the Android app ID, like 1:1234567890:android:abcdef0123456789."];
        if (!FirebaseApiKey.IsMatch(apiKey)) errors["apiKey"] = ["Expected the Android API key (current_key in google-services.json)."];
        if (sender.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(sender, @"^\d{6,20}$"))
            errors["senderId"] = ["Expected the numeric sender ID (project number) or leave it empty."];

        var json = input.ServiceAccountJson is { Length: > 0 } sent ? sent : (input.ServiceAccountJson is null ? storedServiceAccountJson : null);
        if (input.ServiceAccountJson is { Length: > 0 } candidate &&
            (candidate.Length > MaxServiceAccountJsonLength || ServiceAccountEmail(candidate) is null))
            errors["serviceAccountJson"] = ["Paste the full JSON key of a Google service account."];
        else if (!errors.ContainsKey("projectId") && ServiceAccountProjectId(json) is { } keyProject && keyProject != project)
            errors["serviceAccountJson"] = [$"The key belongs to the project '{keyProject}', not '{project}'."];
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
