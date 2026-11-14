using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    /// <summary>True when paid access is required. Off by default: everything is free.</summary>
    Task<bool> GetSubscriptionsEnforcedAsync(CancellationToken ct = default);
    Task<PlayBillingConfig> GetPlayBillingAsync(CancellationToken ct = default);
    Task<PlayBillingView> GetPlayBillingViewAsync(CancellationToken ct = default);
    /// <summary>Operational settings: a valid database value, else the environment, else the default. Invalid values are skipped.</summary>
    Task<OperationalConfig> GetOperationalAsync(CancellationToken ct = default, Action<string, string>? onInvalid = null);
    Task<DietoExpressConfig> GetDietoExpressAsync(CancellationToken ct = default);
    Task<OperationsView> GetOperationsViewAsync(CancellationToken ct = default);
}

public sealed class RuntimeSettings(
    TrazoDbContext db, IConfiguration configuration, ISecretProtector protector, ILogger<RuntimeSettings>? logger = null)
    : IRuntimeSettings
{
    public async Task<OperationalConfig> GetOperationalAsync(CancellationToken ct = default, Action<string, string>? onInvalid = null)
    {
        var rows = await LoadAsync(ct);
        return OperationalResolver.Build(key => Stored(rows, key), key => FromEnvironment(key), onInvalid ?? LogInvalid);
    }

    public async Task<DietoExpressConfig> GetDietoExpressAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(ct);
        return BuildDietoExpress(rows);
    }

    public async Task<OperationsView> GetOperationsViewAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(ct);
        var ops = OperationalResolver.Build(key => Stored(rows, key), key => FromEnvironment(key), LogInvalid);
        var dieto = BuildDietoExpress(rows);
        var source = !string.IsNullOrWhiteSpace(Stored(rows, SettingKeys.DietoExpressApiKey)) ? "database"
            : !string.IsNullOrWhiteSpace(FromEnvironment(SettingKeys.DietoExpressApiKey)) ? "environment" : "none";
        return new OperationsView(
            OperationalResolver.EnvironmentOrigins(configuration), ops.AdditionalCorsOrigins,
            ops.SetupPerMinute, ops.SetupTestPerMinute, ops.SubscriptionPerMinute, ops.LogLevel, ops.LogRetentionDays,
            dieto.Endpoint, !string.IsNullOrWhiteSpace(dieto.ApiKey), source);
    }

    private DietoExpressConfig BuildDietoExpress(Dictionary<string, AppSetting> rows)
    {
        var endpoint = OperationalResolver.Resolve(SettingKeys.DietoExpressEndpoint, key => Stored(rows, key), key => FromEnvironment(key),
            OperationalValidator.TryParseEndpoint, "", LogInvalid);
        return new DietoExpressConfig(endpoint, Get(rows, SettingKeys.DietoExpressApiKey));
    }

    private void LogInvalid(string key, string source) =>
        logger?.LogWarning("The {Source} value of setting {Key} is invalid and was ignored.", source, key);

    private string? FromEnvironment(string key) =>
        ConfigurationBaseline.Get(configuration, key) is { Length: > 0 } value ? value : null;

    private string? Stored(Dictionary<string, AppSetting> rows, string key)
    {
        if (!rows.TryGetValue(key, out var row)) return null;
        var value = row.IsSecret ? protector.Unprotect(row.Value) : row.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

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

    public async Task<bool> GetSubscriptionsEnforcedAsync(CancellationToken ct = default) =>
        bool.TryParse(Get(await LoadAsync(ct), SettingKeys.SubscriptionsEnforced), out var on) && on;

    public async Task<PlayBillingConfig> GetPlayBillingAsync(CancellationToken ct = default)
    {
        var rows = await LoadAsync(ct);
        return new PlayBillingConfig(
            Get(rows, SettingKeys.PlayPackageName) ?? "", Get(rows, SettingKeys.PlayServiceAccountJson),
            Get(rows, SettingKeys.PlayRtdnSecret), Get(rows, SettingKeys.PlayRtdnAudience),
            Get(rows, SettingKeys.PlayRtdnServiceAccountEmail));
    }

    public async Task<PlayBillingView> GetPlayBillingViewAsync(CancellationToken ct = default)
    {
        var c = await GetPlayBillingAsync(ct);
        return new PlayBillingView(c.PackageName, !string.IsNullOrWhiteSpace(c.ServiceAccountJson),
            SettingsValidator.ServiceAccountEmail(c.ServiceAccountJson), !string.IsNullOrWhiteSpace(c.RtdnSecret),
            c.RtdnAudience, c.RtdnServiceAccountEmail, c.IsComplete, c.IsComplete && c.RtdnAuthConfigured);
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

    private string? Get(Dictionary<string, AppSetting> rows, string key) => Stored(rows, key) ?? FromEnvironment(key);
}
