using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Domain.Identity;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Settings;

public interface ISetupService
{
    Task<SetupStatus> GetStatusAsync(CancellationToken ct = default);
    /// <summary>True while the public first-run wizard may be used.</summary>
    Task<bool> IsOpenAsync(CancellationToken ct = default);
    Task<SetupResult> CompleteAsync(SetupInput input, CancellationToken ct = default);
}

/// <summary>
/// First-run setup. The wizard is open only while there is no "setup completed" marker AND no user.
/// Completion inserts the settings, the superadmin and the marker in ONE SaveChanges (a database
/// transaction). The marker row has a primary key, so when two requests race, exactly one commits;
/// the other fails with a key violation and rolls back entirely.
/// </summary>
public sealed class SetupService(
    TrazoDbContext db, IRuntimeSettings runtime, ISettingsWriter writer, PasswordHasher<TrazoUser> hasher)
    : ISetupService
{
    public async Task<bool> IsOpenAsync(CancellationToken ct = default) =>
        !await db.AppSettings.AnyAsync(x => x.Key == SettingKeys.SetupCompletedAt, ct) &&
        !await db.Users.AnyAsync(ct);

    public async Task<SetupStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var open = await IsOpenAsync(ct);
        var google = await runtime.GetGoogleAsync(ct);
        var email = await runtime.GetEmailAsync(ct);
        return new SetupStatus(open, open, !string.IsNullOrWhiteSpace(google.ClientId), email.IsComplete);
    }

    public async Task<SetupResult> CompleteAsync(SetupInput input, CancellationToken ct = default)
    {
        if (!await IsOpenAsync(ct)) return new SetupResult(SetupOutcome.AlreadyCompleted);

        var errors = SettingsValidator.ValidateGoogle(input.Google);
        foreach (var e in SettingsValidator.ValidateEmail(input.Email)) errors[e.Key] = e.Value;
        foreach (var e in SettingsValidator.ValidateAdmin(input.Admin)) errors[e.Key] = e.Value;
        if (errors.Count > 0) return new SetupResult(SetupOutcome.Invalid, errors);

        var now = DateTimeOffset.UtcNow;
        var email = input.Admin.Email.Trim();
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = input.Admin.DisplayName.Trim(),
            Role = "superadmin",
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = hasher.HashPassword(user, input.Admin.Password);

        await writer.StageAsync(input.Google, input.Email, keepPasswordWhenNull: false, ct);
        db.Users.Add(user);
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingKeys.SetupCompletedAt, Value = now.ToString("O"), IsSecret = false, UpdatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request won the race (marker/key violation); nothing of ours was committed.
            db.ChangeTracker.Clear();
            if (!await IsOpenAsync(ct)) return new SetupResult(SetupOutcome.AlreadyCompleted);
            throw;
        }
        return new SetupResult(SetupOutcome.Completed);
    }
}

public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IRuntimeSettings, RuntimeSettings>();
        services.AddScoped<ISettingsWriter, SettingsWriter>();
        services.AddScoped<ISetupService, SetupService>();
        return services;
    }
}
