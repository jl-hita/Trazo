using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Settings;

public interface ISettingsWriter
{
    /// <summary>Adds/updates setting rows in the change tracker. The caller calls SaveChanges.</summary>
    Task StageAsync(GoogleConfig google, EmailConfig email, bool keepPasswordWhenNull, CancellationToken ct);
    /// <summary>Validates and persists configuration (superadmin edits). Returns validation errors, if any.</summary>
    Task<Dictionary<string, string[]>> UpdateAsync(GoogleConfig google, EmailConfig email, CancellationToken ct);
    Task SetSubscriptionsEnforcedAsync(bool enforced, CancellationToken ct);
    /// <summary>Validates and persists the Google Play Billing configuration. Returns validation errors, if any.</summary>
    Task<Dictionary<string, string[]>> UpdatePlayBillingAsync(PlayBillingInput input, CancellationToken ct);
    /// <summary>
    /// Validates and persists the operational settings. Nothing is written when any value is invalid. The result lists
    /// what changed (without secret values) for the audit log.
    /// </summary>
    Task<OperationsUpdateResult> UpdateOperationsAsync(OperationsInput input, CancellationToken ct);
}

public sealed class SettingsWriter(TrazoDbContext db, ISecretProtector protector) : ISettingsWriter
{
    public async Task StageAsync(GoogleConfig google, EmailConfig email, bool keepPasswordWhenNull, CancellationToken ct)
    {
        await Upsert(SettingKeys.GoogleClientId, google.ClientId.Trim(), false, ct);
        await Upsert(SettingKeys.SmtpHost, email.Host.Trim(), false, ct);
        await Upsert(SettingKeys.SmtpPort, email.Port.ToString(CultureInfo.InvariantCulture), false, ct);
        await Upsert(SettingKeys.SmtpEnableSsl, email.EnableSsl ? "true" : "false", false, ct);
        await Upsert(SettingKeys.SmtpUsername, email.Username?.Trim() ?? "", false, ct);
        if (email.Password is not null || !keepPasswordWhenNull)
            await Upsert(SettingKeys.SmtpPassword, email.Password ?? "", true, ct);
        await Upsert(SettingKeys.EmailFrom, email.From.Trim(), false, ct);
        await Upsert(SettingKeys.EmailActionBaseUrl, email.ActionBaseUrl.Trim().TrimEnd('/'), false, ct);
    }

    public async Task<Dictionary<string, string[]>> UpdateAsync(GoogleConfig google, EmailConfig email, CancellationToken ct)
    {
        var errors = SettingsValidator.ValidateGoogle(google);
        foreach (var e in SettingsValidator.ValidateEmail(email)) errors[e.Key] = e.Value;
        if (errors.Count > 0) return errors;
        // Password null = keep the stored one; empty string = clear it.
        await StageAsync(google, email, keepPasswordWhenNull: true, ct);
        await db.SaveChangesAsync(ct);
        return errors;
    }

    public async Task SetSubscriptionsEnforcedAsync(bool enforced, CancellationToken ct)
    {
        await Upsert(SettingKeys.SubscriptionsEnforced, enforced ? "true" : "false", false, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, string[]>> UpdatePlayBillingAsync(PlayBillingInput input, CancellationToken ct)
    {
        var errors = SettingsValidator.ValidatePlayBilling(input);
        if (errors.Count > 0) return errors;
        await Upsert(SettingKeys.PlayPackageName, input.PackageName.Trim(), false, ct);
        if (input.ServiceAccountJson is not null)
            await Upsert(SettingKeys.PlayServiceAccountJson, input.ServiceAccountJson.Trim(), true, ct);
        if (input.RtdnSecret is not null)
            await Upsert(SettingKeys.PlayRtdnSecret, input.RtdnSecret.Trim(), true, ct);
        await Upsert(SettingKeys.PlayRtdnAudience, input.RtdnAudience?.Trim() ?? "", false, ct);
        await Upsert(SettingKeys.PlayRtdnServiceAccountEmail, input.RtdnServiceAccountEmail?.Trim() ?? "", false, ct);
        await db.SaveChangesAsync(ct);
        return errors;
    }

    public async Task<OperationsUpdateResult> UpdateOperationsAsync(OperationsInput input, CancellationToken ct)
    {
        var errors = OperationalValidator.Validate(input);
        if (errors.Count > 0) return new OperationsUpdateResult(errors, []);

        var origins = OperationalValidator.TryParseOrigins(
            OperationalValidator.JoinOrigins(input.AdditionalCorsOrigins ?? []), out var parsed) ? parsed : [];
        OperationalValidator.TryParseLogLevel(input.LogLevel ?? "", out var level);
        OperationalValidator.TryParseEndpoint(input.DietoExpressEndpoint ?? "", out var endpoint);

        var changes = new List<string>();
        void Track(string key, bool changed, string? from, string? to)
        {
            if (changed) changes.Add($"{key}: '{from ?? ""}' -> '{to ?? ""}'");
        }
        async Task Plain(string key, string value)
        {
            var (changed, previous) = await UpsertTracked(key, value, false, ct);
            Track(key, changed, previous, value);
        }

        await Plain(SettingKeys.CorsAdditionalOrigins, OperationalValidator.JoinOrigins(origins));
        await Plain(SettingKeys.RateLimitSetup, input.SetupPerMinute.ToString(CultureInfo.InvariantCulture));
        await Plain(SettingKeys.RateLimitSetupTest, input.SetupTestPerMinute.ToString(CultureInfo.InvariantCulture));
        await Plain(SettingKeys.RateLimitSubscription, input.SubscriptionPerMinute.ToString(CultureInfo.InvariantCulture));
        await Plain(SettingKeys.LogLevelDefault, level ?? "");
        await Plain(SettingKeys.LogRetentionDays, input.LogRetentionDays.ToString(CultureInfo.InvariantCulture));
        await Plain(SettingKeys.DietoExpressEndpoint, endpoint);
        // Null keeps the stored key, empty clears it. The value itself never reaches the audit entry.
        if (input.DietoExpressApiKey is not null)
        {
            await Upsert(SettingKeys.DietoExpressApiKey, input.DietoExpressApiKey.Trim(), true, ct);
            changes.Add($"{SettingKeys.DietoExpressApiKey}: {(input.DietoExpressApiKey.Trim().Length > 0 ? "replaced" : "cleared")} (value not logged)");
        }
        await db.SaveChangesAsync(ct);
        return new OperationsUpdateResult(errors, changes);
    }

    private async Task<(bool Changed, string? Previous)> UpsertTracked(string key, string value, bool secret, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        var previous = row?.Value;
        await Upsert(key, value, secret, ct);
        return (!string.Equals(previous ?? "", value, StringComparison.Ordinal), previous);
    }

    private async Task Upsert(string key, string value, bool secret, CancellationToken ct)
    {
        var stored = secret && value.Length > 0 ? protector.Protect(value) : value;
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = stored, IsSecret = secret, UpdatedAt = DateTimeOffset.UtcNow });
        else
        {
            row.Value = stored; row.IsSecret = secret; row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
