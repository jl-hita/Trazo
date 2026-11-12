using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Settings;

/// <summary>
/// Effective settings: database values (set from the web UI) win over environment/appsettings
/// values. Rows are read on every call, so changes apply immediately without restarting the API.
/// </summary>
public interface IRuntimeSettings
{
    Task<GoogleConfig> GetGoogleAsync(CancellationToken ct = default);
    Task<EmailConfig> GetEmailAsync(CancellationToken ct = default);
    Task<SettingsView> GetViewAsync(CancellationToken ct = default);
}

public sealed class RuntimeSettings(TrazoDbContext db, IConfiguration configuration, ISecretProtector protector)
    : IRuntimeSettings
{
    public async Task<GoogleConfig> GetGoogleAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(ct);
        return new GoogleConfig(Get(rows, SettingKeys.GoogleClientId) ?? "");
    }

    public async Task<EmailConfig> GetEmailAsync(CancellationToken ct = default) =>
        BuildEmail(await LoadAsync(ct));

    public async Task<SettingsView> GetViewAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(ct);
        var e = BuildEmail(rows);
        return new SettingsView(Get(rows, SettingKeys.GoogleClientId) ?? "", e.Host, e.Port, e.EnableSsl,
            e.Username, !string.IsNullOrEmpty(e.Password), e.From, e.ActionBaseUrl);
    }

    private EmailConfig BuildEmail(Dictionary<string, AppSetting> rows)
    {
        var port = int.TryParse(Get(rows, SettingKeys.SmtpPort), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 587;
        var ssl = bool.TryParse(Get(rows, SettingKeys.SmtpEnableSsl), out var s) ? s : true;
        return new EmailConfig(
            Get(rows, SettingKeys.SmtpHost) ?? "", port, ssl,
            Get(rows, SettingKeys.SmtpUsername), Get(rows, SettingKeys.SmtpPassword),
            Get(rows, SettingKeys.EmailFrom) ?? "", Get(rows, SettingKeys.EmailActionBaseUrl)?.TrimEnd('/') ?? "");
    }

    private async Task<Dictionary<string, AppSetting>> LoadAsync(CancellationToken ct) =>
        await db.AppSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, ct);

    private string? Get(Dictionary<string, AppSetting> rows, string key)
    {
        if (rows.TryGetValue(key, out var row))
        {
            var value = row.IsSecret ? protector.Unprotect(row.Value) : row.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return configuration[key] is { Length: > 0 } fallback ? fallback : null;
    }
}
