using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Domain.Settings;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Planning;

namespace Trazo.Infrastructure.Accounts;

/// <summary>
/// The entity types of the model, split by what account deletion does with them. A test walks the EF model and fails when a
/// type is in neither list, so a new table with user data cannot be forgotten (docs/account-deletion.md).
/// </summary>
public static class AccountData
{
    /// <summary>Tables with data about a user, directly (a <c>UserId</c> column) or through their plan. All are deleted with the account.</summary>
    public static readonly IReadOnlySet<Type> UserOwned = new HashSet<Type>
    {
        typeof(TrazoUser), typeof(RefreshToken), typeof(AuthChallenge), typeof(AuthRecoveryCode), typeof(EmailActionToken),
        typeof(Subscription), typeof(TrainingPlan), typeof(TrainingPlanPhase), typeof(TrainingMesocycle), typeof(PlannedSession),
        typeof(WorkoutLog), typeof(WorkoutSetLog), typeof(PlanAdaptation)
    };

    /// <summary>Tables that hold no personal data (shared catalog and application settings).</summary>
    public static readonly IReadOnlySet<Type> NotPersonal = new HashSet<Type> { typeof(ExerciseCatalogItem), typeof(AppSetting) };
}

public enum AccountDeletionBlock { NotFound, LastSuperadmin }

/// <summary>Result of a deletion. <see cref="SubjectHash"/> identifies the account in the audit log without being reversible.</summary>
public sealed record AccountDeletionResult(bool Deleted, AccountDeletionBlock? Blocked, int RowsDeleted, string SubjectHash, bool PlaySubscriptionNotCancelled);

public sealed record PlaySubscriptionNotice(string Status, DateTimeOffset ExpiresAt, bool AutoRenewing);

/// <summary>What the app needs before it offers account deletion.</summary>
public sealed record AccountInfo(
    Guid Id, string Email, string DisplayName, string Role, string TimeZone, bool HasPassword, bool GoogleLinked, bool TwoFactorEnabled,
    bool CanDelete, string? CannotDeleteReason, DeletionRequirements Deletion, PlaySubscriptionNotice? PlaySubscription);

/// <summary>
/// What a deletion request must prove. <c>RequiresPassword</c> and <c>RequiresGoogleIdToken</c> are alternatives (one is enough);
/// the two-factor code is required in addition when the account has it.
/// </summary>
public sealed record DeletionRequirements(
    bool RequiresPassword, bool RequiresGoogleIdToken, bool RequiresTwoFactorCode, bool PlaySubscriptionNotCancelled, string? Warning, string? ManageSubscriptionUrl);

public interface IAccountService
{
    Task<AccountInfo?> GetInfoAsync(Guid userId, CancellationToken ct);
    Task<AccountExport?> ExportAsync(Guid userId, CancellationToken ct);
    Task<AccountDeletionResult> DeleteAsync(Guid userId, CancellationToken ct);
    /// <summary>Saves the IANA time zone. Returns false when it is not a valid zone or the user does not exist.</summary>
    Task<bool> SetTimeZoneAsync(Guid userId, string timeZone, CancellationToken ct);
}

/// <summary>
/// Account information, the data export (right of access and portability) and the deletion of the account with all of its
/// data (right to erasure). Deletion is explicit and transactional: either every row of the user disappears or none does.
/// </summary>
public sealed class AccountService(TrazoDbContext db, TimeProvider clock) : IAccountService
{
    public const string ManageSubscriptionsUrl = "https://play.google.com/store/account/subscriptions";
    private const string SuperadminRole = "superadmin";
    private static readonly SubscriptionStatus[] StillBilling =
        [SubscriptionStatus.Trial, SubscriptionStatus.Active, SubscriptionStatus.GracePeriod, SubscriptionStatus.OnHold];

    // ---------------------------------------------------------------- information

    public async Task<AccountInfo?> GetInfoAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        var notice = await PlaySubscriptionAsync(userId, ct);
        var lastSuperadmin = await IsLastSuperadminAsync(user, ct);
        var zone = UserTimeZone.TryParse(user.TimeZoneId, out _) ? user.TimeZoneId! : UserTimeZone.DefaultId;
        var warning = notice is null ? null
            : "Tu suscripción de Google Play NO se cancela al eliminar la cuenta: seguirás pagando hasta que la canceles en Google Play.";
        return new AccountInfo(
            user.Id, user.Email, user.DisplayName, user.Role, zone, user.PasswordHash is not null, user.GoogleSubject is not null,
            user.TwoFactorEnabled, !lastSuperadmin,
            lastSuperadmin ? "Eres el último superadministrador: nombra a otro antes de eliminar la cuenta." : null,
            new DeletionRequirements(user.PasswordHash is not null, user.GoogleSubject is not null && user.PasswordHash is null,
                user.TwoFactorEnabled, notice is not null, warning, notice is null ? null : ManageSubscriptionsUrl),
            notice);
    }

    private async Task<PlaySubscriptionNotice?> PlaySubscriptionAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId && s.AutoRenewing).ToListAsync(ct);
        var billing = rows.Where(s => StillBilling.Contains(s.Status) && (s.ExpiresAt > now || s.Status == SubscriptionStatus.OnHold))
            .OrderByDescending(s => s.ExpiresAt).FirstOrDefault();
        return billing is null ? null : new PlaySubscriptionNotice(billing.Status.ToString(), billing.ExpiresAt, billing.AutoRenewing);
    }

    private async Task<bool> IsLastSuperadminAsync(TrazoUser user, CancellationToken ct) =>
        user.Role == SuperadminRole
        && !await db.Users.AnyAsync(x => x.Role == SuperadminRole && x.DisabledAt == null && x.Id != user.Id, ct);

    // ---------------------------------------------------------------- time zone

    public async Task<bool> SetTimeZoneAsync(Guid userId, string timeZone, CancellationToken ct)
    {
        if (!UserTimeZone.TryParse(timeZone, out _)) return false;
        var changed = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TimeZoneId, timeZone.Trim()).SetProperty(u => u.UpdatedAt, clock.GetUtcNow()), ct);
        return changed == 1;
    }

    // ---------------------------------------------------------------- deletion

    public async Task<AccountDeletionResult> DeleteAsync(Guid userId, CancellationToken ct)
    {
        var hash = SubjectHash(userId);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new(false, AccountDeletionBlock.NotFound, 0, hash, false);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Checked inside the transaction so two superadmins deleting each other at once cannot leave none.
        if (await IsLastSuperadminAsync(user, ct)) return new(false, AccountDeletionBlock.LastSuperadmin, 0, hash, false);
        var playPending = await PlaySubscriptionAsync(userId, ct) is not null;

        var plans = db.TrainingPlans.Where(p => p.UserId == userId).Select(p => p.Id);
        var rows = 0;
        // Children first. Every step deletes and then checks that nothing is left.
        rows += await StepAsync(db.WorkoutSetLogs.Where(s => db.WorkoutLogs.Any(l => l.Id == s.WorkoutLogId && l.UserId == userId)), ct);
        rows += await StepAsync(db.WorkoutLogs.Where(l => l.UserId == userId), ct);
        rows += await StepAsync(db.PlanAdaptations.Where(a => plans.Contains(a.PlanId)), ct);
        rows += await StepAsync(db.PlannedSessions.Where(s => plans.Contains(s.PlanId)), ct);
        rows += await StepAsync(db.TrainingMesocycles.Where(m => plans.Contains(m.PlanId)), ct);
        rows += await StepAsync(db.TrainingPlanPhases.Where(p => plans.Contains(p.PlanId)), ct);
        rows += await StepAsync(db.TrainingPlans.Where(p => p.UserId == userId), ct);
        rows += await StepAsync(db.Subscriptions.Where(s => s.UserId == userId), ct);
        rows += await StepAsync(db.RefreshTokens.Where(t => t.UserId == userId), ct);
        rows += await StepAsync(db.AuthChallenges.Where(c => c.UserId == userId), ct);
        rows += await StepAsync(db.AuthRecoveryCodes.Where(c => c.UserId == userId), ct);
        rows += await StepAsync(db.EmailActionTokens.Where(t => t.UserId == userId), ct);
        rows += await StepAsync(db.Users.Where(u => u.Id == userId), ct);
        await transaction.CommitAsync(ct);

        await ScrubLogsAsync(userId, ct);
        return new(true, null, rows, hash, playPending);
    }

    private static async Task<int> StepAsync<T>(IQueryable<T> rows, CancellationToken ct) where T : class
    {
        var deleted = await rows.ExecuteDeleteAsync(ct);
        if (await rows.AnyAsync(ct))
            throw new InvalidOperationException($"Account deletion left rows in {typeof(T).Name}; the transaction was rolled back.");
        return deleted;
    }

    /// <summary>
    /// The application log never stores emails, but a few messages carry the user's id (for example a rejected purchase).
    /// Those lines are removed so the id no longer points anywhere. Best effort and PostgreSQL only: the log table is not part
    /// of the EF model, and logs are in any case purged after the retention period (30 days by default).
    /// </summary>
    private async Task ScrubLogsAsync(Guid userId, CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true) return;
        try
        {
            var pattern = $"%{userId}%";
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_logs WHERE message LIKE {pattern} OR exception LIKE {pattern}", ct);
        }
        catch (Exception)
        {
            // Logs are purged by retention anyway; failing here must not undo a completed erasure.
        }
    }

    /// <summary>A short, non-reversible tag for the audit log (the user id itself is never logged for a deleted account).</summary>
    public static string SubjectHash(Guid userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"trazo-account-deletion:{userId}")))[..16].ToLowerInvariant();

    // ---------------------------------------------------------------- export

    public async Task<AccountExport?> ExportAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var plans = await db.TrainingPlans.AsNoTracking().Include(p => p.Phases).Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var planIds = plans.Select(p => p.Id).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => planIds.Contains(s.PlanId)).OrderBy(s => s.Date).ToListAsync(ct);
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.UserId == userId).OrderBy(l => l.LoggedAt).ToListAsync(ct);
        var adaptations = await db.PlanAdaptations.AsNoTracking().Where(a => planIds.Contains(a.PlanId)).OrderBy(a => a.AppliedAt).ToListAsync(ct);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.StartedAt).ToListAsync(ct);
        var devices = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt).ToListAsync(ct);

        return new AccountExport(
            clock.GetUtcNow(), "1",
            new ExportAccount(user.Id, user.Email, user.DisplayName, user.Role, user.EmailVerifiedAt, user.PasswordHash is not null,
                user.GoogleSubject is not null, user.TwoFactorEnabled, user.TimeZoneId, user.CreatedAt, user.LastLoginAt),
            plans.Select(p => new ExportPlan(
                p.Id, p.Status.ToString(), p.Goal, p.Experience, p.Split, p.StartDate, p.TotalWeeks, p.TrainingDays, p.MaxSessionMinutes,
                p.AvailableEquipment, p.ExcludedExerciseIds, p.Limitations, p.IncludeDeloadPhases, p.DeloadEveryTrainingWeeks,
                p.MacrocycleName, p.MacrocycleNotes, p.CreatedAt,
                p.Phases.OrderBy(x => x.SequenceNumber).Select(x => new ExportPhase(x.SequenceNumber, x.Name, x.PhaseType, x.StartDate, x.EndDate, x.Objective)).ToList(),
                sessions.Where(s => s.PlanId == p.Id).Select(s => new ExportSession(
                    s.Id, s.WeekNumber, s.Date, s.Title, s.Status.ToString(), s.EstimatedDurationMinutes, ParseJson(s.PrescriptionJson))).ToList()))
                .ToList(),
            logs.Select(l => new ExportWorkoutLog(
                l.Id, l.PlanId, l.PlannedSessionId, l.LoggedAt, l.Outcome.ToString(), l.NonCompletionReason, l.Recovery, l.SessionRpe,
                l.PainReported, l.Notes,
                l.Sets.OrderBy(s => s.ExerciseId, StringComparer.Ordinal).ThenBy(s => s.SetNumber)
                    .Select(s => new ExportSet(s.ExerciseId, s.SetNumber, s.WeightKg, s.Repetitions, s.Rir, s.Completed, s.SkippedReason, s.PainReported)).ToList()))
                .ToList(),
            adaptations.Select(a => new ExportAdaptation(a.PlanId, a.WorkoutLogId, a.Kind, a.ExerciseIds, a.SessionsChanged, a.Explanation, a.AppliedAt)).ToList(),
            subscriptions.Select(s => new ExportSubscription(
                s.Source, s.ProductId, s.Status.ToString(), s.StartedAt, s.ExpiresAt, s.AutoRenewing, s.LatestOrderId, s.IsTestPurchase)).ToList(),
            devices.Select(d => new ExportDevice(d.DeviceName, d.CreatedAt, d.ExpiresAt, d.RevokedAt)).ToList());
    }

    private static JsonElement ParseJson(string json)
    {
        try { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(json); }
    }
}

// The export contains the user's own data only. It never contains password hashes, the authenticator key, recovery codes,
// refresh or action tokens, or Google Play purchase tokens.
public sealed record AccountExport(
    DateTimeOffset GeneratedAt, string Version, ExportAccount Account, IReadOnlyList<ExportPlan> Plans,
    IReadOnlyList<ExportWorkoutLog> WorkoutLogs, IReadOnlyList<ExportAdaptation> Adaptations,
    IReadOnlyList<ExportSubscription> Subscriptions, IReadOnlyList<ExportDevice> Devices);

public sealed record ExportAccount(
    Guid Id, string Email, string DisplayName, string Role, DateTimeOffset? EmailVerifiedAt, bool HasPassword, bool GoogleLinked,
    bool TwoFactorEnabled, string? TimeZone, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record ExportPlan(
    Guid Id, string Status, string Goal, string Experience, string Split, DateOnly StartDate, int TotalWeeks, string[] TrainingDays,
    int MaxSessionMinutes, string[] AvailableEquipment, string[] ExcludedExerciseIds, string[] Limitations, bool IncludeDeloadPhases,
    int DeloadEveryTrainingWeeks, string MacrocycleName, string MacrocycleNotes, DateTimeOffset CreatedAt,
    IReadOnlyList<ExportPhase> Phases, IReadOnlyList<ExportSession> Sessions);

public sealed record ExportPhase(int Sequence, string Name, string Type, DateOnly StartDate, DateOnly EndDate, string Objective);
public sealed record ExportSession(Guid Id, int Week, DateOnly Date, string Title, string Status, int EstimatedDurationMinutes, JsonElement Prescription);

public sealed record ExportWorkoutLog(
    Guid Id, Guid PlanId, Guid SessionId, DateTimeOffset LoggedAt, string Outcome, string NonCompletionReason, string Recovery,
    decimal? SessionRpe, bool PainReported, string? Notes, IReadOnlyList<ExportSet> Sets);

public sealed record ExportSet(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool PainReported);
public sealed record ExportAdaptation(Guid PlanId, Guid? WorkoutLogId, string Kind, string[] ExerciseIds, int SessionsChanged, string Explanation, DateTimeOffset AppliedAt);
public sealed record ExportSubscription(string Source, string ProductId, string Status, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, bool AutoRenewing, string? LatestOrderId, bool IsTestPurchase);
public sealed record ExportDevice(string? DeviceName, DateTimeOffset SignedInAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
