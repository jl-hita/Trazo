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
