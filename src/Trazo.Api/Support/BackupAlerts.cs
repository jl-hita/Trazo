using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Identity;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;

namespace Trazo.Api.Support;

public sealed record BackupAlertCandidate(Guid Id, string DisplayName, string Email, string Role, int Devices);

/// <param name="RecipientUserId">Null when nobody is chosen (or the chosen account no longer qualifies).</param>
public sealed record BackupAlertView(
    Guid? RecipientUserId, string? RecipientName, int RecipientDevices, IReadOnlyList<BackupAlertCandidate> Candidates);

/// <summary>
/// Who receives the push notification when backups go wrong. Stored in the settings table as the id of the account (the name is
/// looked up when shown, so renaming the account or deleting it never leaves a stale copy of personal data here). Only enabled,
/// verified staff accounts can be chosen: the notice says nothing sensitive, but it is operational and belongs to the team.
/// </summary>
public sealed class BackupAlertSettings(TrazoDbContext db, TimeProvider clock)
{
    public const string RecipientKey = "backups.alert_user_id";
    public const string StateKey = "backups.alert_state";

    private IQueryable<TrazoUser> Staff() => db.Users.AsNoTracking()
        .Where(u => (u.Role == "admin" || u.Role == "superadmin") && u.DisabledAt == null && u.EmailVerifiedAt != null);

    /// <summary>The chosen account, only while it still qualifies (a disabled or deleted account stops receiving).</summary>
    public async Task<Guid?> GetRecipientIdAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(await GetAsync(RecipientKey, ct), out var id)) return null;
        return await Staff().AnyAsync(u => u.Id == id, ct) ? id : null;
    }

    public async Task<BackupAlertView> GetViewAsync(CancellationToken ct)
    {
        var staff = await Staff().OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Role }).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();
        var devices = await db.DeviceTokens.AsNoTracking().Where(d => ids.Contains(d.UserId))
            .GroupBy(d => d.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var candidates = staff.Select(s => new BackupAlertCandidate(s.Id, s.DisplayName, s.Email, s.Role, devices.GetValueOrDefault(s.Id))).ToList();
        var chosen = Guid.TryParse(await GetAsync(RecipientKey, ct), out var id) ? candidates.FirstOrDefault(c => c.Id == id) : null;
        return new BackupAlertView(chosen?.Id, chosen?.DisplayName, chosen?.Devices ?? 0, candidates);
    }

    /// <summary>Chooses the recipient (null turns the notices off). False when the account is not an enabled staff account.</summary>
    public async Task<bool> SetRecipientAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is { } id && !await Staff().AnyAsync(u => u.Id == id, ct)) return false;
        await SetAsync(RecipientKey, userId?.ToString(), ct);
        // A new recipient starts clean: whatever was already reported to the previous one is reported again to this one.
        await SetAsync(StateKey, null, ct);
        return true;
    }

    public async Task<BackupAlertState?> GetStateAsync(CancellationToken ct)
    {
        var raw = await GetAsync(StateKey, ct);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<BackupAlertState>(raw, Json); }
        catch (JsonException) { return null; }
    }

    public Task SaveStateAsync(BackupAlertState? state, CancellationToken ct) =>
        SetAsync(StateKey, state is null ? null : JsonSerializer.Serialize(state, Json), ct);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<string?> GetAsync(string key, CancellationToken ct) =>
        await db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    private async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (value is null)
        {
            if (row is not null) db.AppSettings.Remove(row);
        }
        else if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value, IsSecret = false, UpdatedAt = clock.GetUtcNow() });
        else { row.Value = value; row.UpdatedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>What was last reported, so a failure that lasts is not announced again at every check (nor after every deploy).</summary>
public sealed record BackupAlertState(string Fingerprint, DateTimeOffset SentAt);

public enum BackupAlertOutcome { Nothing, Alerted, Recovered, NoDevices, PushFailed }

/// <summary>
/// One round of the backup watch: reads the same status files the console panel shows and, when the backups are in error, sends a
/// push notification to the chosen account (the app builds it from the server's text, as for any kind it does not know). The same
/// problem is repeated at most once a day; when the backups are healthy again it says so once. The text never carries anything but
/// the state of the backups.
/// </summary>
public sealed class BackupAlerter(
    BackupAlertSettings settings, IPushSender push, IConfiguration configuration, TimeProvider clock, ILogger<BackupAlerter> logger)
{
    public static readonly TimeSpan Reminder = TimeSpan.FromHours(24);
    public const string Title = "TRAZO backup";

    public async Task<BackupAlertOutcome> RunOnceAsync(CancellationToken ct)
    {
        if (await settings.GetRecipientIdAsync(ct) is not { } recipient) return BackupAlertOutcome.Nothing;
        var now = clock.GetUtcNow();
        var view = BackupStatusReader.Read(configuration["Backups:StatusPath"], now);
        if (!view.Configured) return BackupAlertOutcome.Nothing;
        var state = await settings.GetStateAsync(ct);

        if (view.Health == "error")
        {
            var fingerprint = $"{view.Message}|{view.Copies.FirstOrDefault()?.Name}|{view.LastRun?.FinishedAt:O}";
            if (state is not null && state.Fingerprint == fingerprint && now - state.SentAt < Reminder) return BackupAlertOutcome.Nothing;
            var sent = await SendAsync(recipient, $"{view.Message} Revisa «Copias de seguridad» en la consola.", "backup-alert", ct);
            if (sent != BackupAlertOutcome.PushFailed) await settings.SaveStateAsync(new BackupAlertState(fingerprint, now), ct);
            return sent == BackupAlertOutcome.Nothing ? BackupAlertOutcome.Alerted : sent;
        }

        if (view.Health == "healthy" && state is not null)
        {
            var sent = await SendAsync(recipient, "Las copias de seguridad vuelven a estar al día.", "backup-ok", ct);
            if (sent != BackupAlertOutcome.PushFailed) await settings.SaveStateAsync(null, ct);
            return sent == BackupAlertOutcome.Nothing ? BackupAlertOutcome.Recovered : sent;
        }
        return BackupAlertOutcome.Nothing;
    }

    /// <summary>Nothing = delivered to at least one device. Push being unavailable does not stop the watch: it tries again next round.</summary>
    private async Task<BackupAlertOutcome> SendAsync(Guid recipient, string body, string kind, CancellationToken ct)
    {
        try
        {
            var result = await push.SendAsync([recipient], new PushMessage(Title, body, kind), ct);
            if (result.Devices == 0)
            {
                logger.LogWarning("The backup notice has no device to go to: the chosen account has not registered the app for notifications.");
                return BackupAlertOutcome.NoDevices;
            }
            return result.Sent > 0 ? BackupAlertOutcome.Nothing : BackupAlertOutcome.PushFailed;
        }
        catch (PushException ex)
        {
            // Not configured, refused credentials and Firebase being unreachable are conditions the superadmin fixes or waits out;
            // Firebase answering with an error is a fault and is logged as an error (an error ticket).
            if (ex.Failure != PushFailure.Unavailable || ex.InnerException is not null)
                logger.LogWarning("The backup notice could not be sent ({Failure}); it will be tried again.", ex.Failure);
            else logger.LogError(ex, "The backup notice could not be sent; it will be tried again.");
            return BackupAlertOutcome.PushFailed;
        }
    }
}

/// <summary>Runs <see cref="BackupAlerter"/> every 30 minutes (the status files change at most once a day, so this is cheap).</summary>
public sealed class BackupAlertWorker(IServiceScopeFactory scopes, ILogger<BackupAlertWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<BackupAlerter>().RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (ex is System.Data.Common.DbException or TimeoutException)
                        logger.LogWarning(ex, "The backup watch could not run; it will run again.");
                    else logger.LogError(ex, "The backup watch failed; it will run again.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
