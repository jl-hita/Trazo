using Microsoft.Extensions.Logging;
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
        typeof(WorkoutLog), typeof(WorkoutSetLog), typeof(PlanAdaptation), typeof(PlanReviewRequest), typeof(PlanReviewMessage),
        typeof(MuscleWeekObservation), typeof(LongitudinalDecision), typeof(FrequencyProposal),
        typeof(Trazo.Domain.Push.DeviceToken),
        // The Premium-time ledger is deleted; funnel events are anonymised (their user id is cleared, the rest holds no personal data).
        typeof(Trazo.Domain.Growth.PremiumGrant), typeof(Trazo.Domain.Growth.GrowthEvent),
        typeof(Trazo.Domain.Growth.ReferralCode), typeof(Trazo.Domain.Growth.ReferralRelationship),
        // The campaign a person came from is deleted with them.
        typeof(Trazo.Domain.Marketing.CampaignAttribution),
        // Not linked to the account: matched by the account's email (the person may have joined the waitlist before registering).
        typeof(Trazo.Domain.Waitlist.WaitlistEntry)
    };

    /// <summary>
    /// Tables that hold no personal data (shared catalog, application settings and anonymous error tickets: they carry no user
    /// and their stack traces are scrubbed of emails, ids and tokens before they are stored).
    /// </summary>
    public static readonly IReadOnlySet<Type> NotPersonal = new HashSet<Type>
    {
        typeof(ExerciseCatalogItem), typeof(ExerciseCatalogTranslation), typeof(AppSetting), typeof(Trazo.Domain.Support.ErrorTicket), typeof(Trazo.Domain.Support.ErrorReportOutboxItem),
        typeof(Trazo.Domain.Analytics.PaywallView),
        // Marketing campaigns, their anonymous daily counters and the owner-entered spend: no user in any of them.
        typeof(Trazo.Domain.Marketing.MarketingCampaign), typeof(Trazo.Domain.Marketing.CampaignDailyStat), typeof(Trazo.Domain.Marketing.CampaignSpend), typeof(Trazo.Domain.Marketing.StoreDownloads),
        // Business books of the owner: they refer to suppliers and customers, never to app users.
        typeof(Trazo.Domain.Fiscal.FiscalEntry), typeof(Trazo.Domain.Fiscal.FiscalClosure), typeof(Trazo.Domain.Fiscal.FiscalAuditEntry)
    };
}

public enum AccountDeletionBlock { NotFound, LastSuperadmin }

/// <summary>Result of a deletion. <see cref="SubjectHash"/> identifies the account in the audit log without being reversible.</summary>
public sealed record AccountDeletionResult(bool Deleted, AccountDeletionBlock? Blocked, int RowsDeleted, string SubjectHash, bool PlaySubscriptionNotCancelled);

public sealed record PlaySubscriptionNotice(string Status, DateTimeOffset ExpiresAt, bool AutoRenewing);

/// <summary>What the app needs before it offers account deletion.</summary>
public sealed record AccountInfo(
    Guid Id, string Email, string DisplayName, string Role, string TimeZone, bool HasPassword, bool GoogleLinked, bool TwoFactorEnabled,
    bool CanDelete, string? CannotDeleteReason, DeletionRequirements Deletion, PlaySubscriptionNotice? PlaySubscription, string? Language = null);

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
    /// <summary>Saves the language the user chose ("es", "en", or null for automatic).</summary>
    Task<bool> SetLanguageAsync(Guid userId, string? language, CancellationToken ct);
}

/// <summary>
/// Account information, the data export (right of access and portability) and the deletion of the account with all of its
/// data (right to erasure). Deletion is explicit and transactional: either every row of the user disappears or none does.
/// </summary>
public sealed class AccountService(TrazoDbContext db, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<AccountService>? logger = null) : IAccountService
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
            notice, user.Language);
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

    public async Task<bool> SetLanguageAsync(Guid userId, string? language, CancellationToken ct)
    {
        if (!Trazo.Application.Localization.LanguageNegotiation.TryNormalizeChoice(language, out var stored)) return false;
        var changed = await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Language, stored).SetProperty(u => u.UpdatedAt, clock.GetUtcNow()), ct);
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
        // Manual plan reviews. The user's own conversations (which may hold health information) are deleted. When the account
        // belongs to an administrator, the conversations of other users stay: only the reference to this administrator goes.
        var reviews = db.PlanReviewRequests.Where(r => r.UserId == userId).Select(r => r.Id);
        await db.PlanReviewMessages.Where(m => m.AuthorUserId == userId && !reviews.Contains(m.RequestId))
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.AuthorUserId, (Guid?)null), ct);
        await db.PlanReviewRequests.Where(r => r.AssignedToUserId == userId && r.UserId != userId && r.Status == PlanReviewStatus.InProgress)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, PlanReviewStatus.Pending), ct);
        await db.PlanReviewRequests.Where(r => r.AssignedToUserId == userId && r.UserId != userId)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.AssignedToUserId, (Guid?)null).SetProperty(r => r.AssignedAt, (DateTimeOffset?)null), ct);
        rows += await StepAsync(db.PlanReviewMessages.Where(m => reviews.Contains(m.RequestId)), ct);
        rows += await StepAsync(db.PlanReviewRequests.Where(r => r.UserId == userId), ct);
        rows += await StepAsync(db.WorkoutSetLogs.Where(s => db.WorkoutLogs.Any(l => l.Id == s.WorkoutLogId && l.UserId == userId)), ct);
        rows += await StepAsync(db.WorkoutLogs.Where(l => l.UserId == userId), ct);
        rows += await StepAsync(db.FrequencyProposals.Where(p => p.UserId == userId), ct);
        rows += await StepAsync(db.LongitudinalDecisions.Where(d => d.UserId == userId), ct);
        rows += await StepAsync(db.MuscleWeekObservations.Where(o => o.UserId == userId), ct);
        rows += await StepAsync(db.PlanAdaptations.Where(a => plans.Contains(a.PlanId)), ct);
        rows += await StepAsync(db.PlannedSessions.Where(s => plans.Contains(s.PlanId)), ct);
        rows += await StepAsync(db.TrainingMesocycles.Where(m => plans.Contains(m.PlanId)), ct);
        rows += await StepAsync(db.TrainingPlanPhases.Where(p => plans.Contains(p.PlanId)), ct);
        rows += await StepAsync(db.TrainingPlans.Where(p => p.UserId == userId), ct);
        rows += await StepAsync(db.Subscriptions.Where(s => s.UserId == userId), ct);
        // Funnel events keep no person: the user id is cleared and the anonymous row still counts in the aggregates.
        rows += await db.GrowthEvents.Where(e => e.UserId == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.UserId, (Guid?)null), ct);
        // A reward earned by someone else because of this account stays theirs; only the link to this person goes.
        await db.PremiumGrants.Where(g => g.OriginUserId == userId && g.UserId != userId)
            .ExecuteUpdateAsync(u => u.SetProperty(g => g.OriginUserId, (Guid?)null).SetProperty(g => g.OriginSubscriptionId, (Guid?)null), ct);
        rows += await StepAsync(db.PremiumGrants.Where(g => g.UserId == userId), ct);
        rows += await StepAsync(db.ReferralRelationships.Where(r => r.ReferrerUserId == userId || r.ReferredUserId == userId), ct);
        rows += await StepAsync(db.ReferralCodes.Where(c => c.UserId == userId), ct);
        // Which campaign the person came from is theirs; the campaign itself and its anonymous counters stay.
        rows += await StepAsync(db.CampaignAttributions.Where(a => a.UserId == userId), ct);
        rows += await StepAsync(db.RefreshTokens.Where(t => t.UserId == userId), ct);
        rows += await StepAsync(db.DeviceTokens.Where(t => t.UserId == userId), ct);
        rows += await StepAsync(db.AuthChallenges.Where(c => c.UserId == userId), ct);
        rows += await StepAsync(db.AuthRecoveryCodes.Where(c => c.UserId == userId), ct);
        rows += await StepAsync(db.EmailActionTokens.Where(t => t.UserId == userId), ct);
        var waitlistEmail = Trazo.Infrastructure.Waitlist.WaitlistService.Normalize(user.Email);
        rows += await StepAsync(db.WaitlistEntries.Where(w => w.Email == waitlistEmail), ct);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logs are purged by retention anyway, so failing here must not undo a completed erasure; but the erasure of personal data is
            // not silently incomplete either: it is logged as an error, which becomes a ticket.
            logger?.LogError(ex, "The logs of a deleted account could not be scrubbed.");
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
        var decisions = (await db.LongitudinalDecisions.AsNoTracking().Where(d => d.UserId == userId).ToListAsync(ct))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Muscle, StringComparer.Ordinal).ToList();
        var observations = (await db.MuscleWeekObservations.AsNoTracking().Where(o => o.UserId == userId).ToListAsync(ct))
            .OrderBy(o => o.PlanId).ThenBy(o => o.Period).ThenBy(o => o.Muscle, StringComparer.Ordinal).ToList();
        var proposals = (await db.FrequencyProposals.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct)).OrderBy(p => p.CreatedAt).ToList();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.StartedAt).ToListAsync(ct);
        var devices = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt).ToListAsync(ct);
        var premiumGrants = await db.PremiumGrants.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.GrantedAt).ToListAsync(ct);

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
                    s.Id, s.WeekNumber, s.Date, s.Title, s.Status.ToString(), s.EstimatedDurationMinutes, ParseJson(s.PrescriptionJson))).ToList(),
                p.MusclePriorities))
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
            devices.Select(d => new ExportDevice(d.DeviceName, d.CreatedAt, d.ExpiresAt, d.RevokedAt)).ToList(),
            premiumGrants.Select(g => new ExportPremiumGrant(g.Kind, g.Days, g.Status, g.GrantedAt, g.StartsAt, g.EndsAt)).ToList(),
            decisions.Select(d => new ExportLongitudinalDecision(
                d.PlanId, d.MesocycleSequence, d.Muscle, d.Action, d.BaselineSets, d.PreviousSets, d.ProposedSets,
                d.AnalyzedFromPeriod, d.AnalyzedToPeriod, d.Reason, d.PolicyVersion, d.CreatedAt)).ToList(),
            observations.Select(o => new ExportMuscleObservation(
                o.PlanId, o.Period, o.Muscle, o.Quality, o.Reason, o.PrescribedSets, o.PerformedSets, o.Exposures, o.PerformanceChangePercent,
                o.FatigueSignals, o.PainSessions)).ToList(),
            proposals.Select(p => new ExportFrequencyProposal(
                p.PlanId, p.Kind, p.Status, p.FromDays, p.ToDays, p.WeeksAnalysed, p.LowWeeks, p.Reason, p.PolicyVersion, p.CreatedAt, p.DecidedAt)).ToList());
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
    IReadOnlyList<ExportSubscription> Subscriptions, IReadOnlyList<ExportDevice> Devices,
    IReadOnlyList<ExportPremiumGrant> PremiumGrants,
    IReadOnlyList<ExportLongitudinalDecision>? LongitudinalDecisions = null,
    IReadOnlyList<ExportMuscleObservation>? MuscleObservations = null,
    IReadOnlyList<ExportFrequencyProposal>? FrequencyProposals = null);

public sealed record ExportFrequencyProposal(
    Guid PlanId, string Kind, string Status, string FromDays, string ToDays, int WeeksAnalysed, int LowWeeks, string Reason,
    string PolicyVersion, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed record ExportLongitudinalDecision(
    Guid PlanId, int MesocycleSequence, string Muscle, string Action, decimal BaselineSets, decimal PreviousSets, decimal ProposedSets,
    int AnalyzedFromPeriod, int AnalyzedToPeriod, string Reason, string PolicyVersion, DateTimeOffset CreatedAt);

public sealed record ExportMuscleObservation(
    Guid PlanId, int Period, string Muscle, string Quality, string? Reason, decimal PrescribedSets, decimal PerformedSets, int Exposures,
    decimal? PerformanceChangePercent, int FatigueSignals, int PainSessions);

public sealed record ExportPremiumGrant(string Kind, int Days, string Status, DateTimeOffset GrantedAt, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);

public sealed record ExportAccount(
    Guid Id, string Email, string DisplayName, string Role, DateTimeOffset? EmailVerifiedAt, bool HasPassword, bool GoogleLinked,
    bool TwoFactorEnabled, string? TimeZone, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record ExportPlan(
    Guid Id, string Status, string Goal, string Experience, string Split, DateOnly StartDate, int TotalWeeks, string[] TrainingDays,
    int MaxSessionMinutes, string[] AvailableEquipment, string[] ExcludedExerciseIds, string[] Limitations, bool IncludeDeloadPhases,
    int DeloadEveryTrainingWeeks, string MacrocycleName, string MacrocycleNotes, DateTimeOffset CreatedAt,
    IReadOnlyList<ExportPhase> Phases, IReadOnlyList<ExportSession> Sessions, string[]? MusclePriorities = null);

public sealed record ExportPhase(int Sequence, string Name, string Type, DateOnly? StartDate, DateOnly? EndDate, string Objective);
public sealed record ExportSession(Guid Id, int Week, DateOnly? Date, string Title, string Status, int EstimatedDurationMinutes, JsonElement Prescription);

public sealed record ExportWorkoutLog(
    Guid Id, Guid PlanId, Guid SessionId, DateTimeOffset LoggedAt, string Outcome, string NonCompletionReason, string Recovery,
    decimal? SessionRpe, bool PainReported, string? Notes, IReadOnlyList<ExportSet> Sets);

public sealed record ExportSet(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool PainReported);
public sealed record ExportAdaptation(Guid PlanId, Guid? WorkoutLogId, string Kind, string[] ExerciseIds, int SessionsChanged, string Explanation, DateTimeOffset AppliedAt);
public sealed record ExportSubscription(string Source, string ProductId, string Status, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, bool AutoRenewing, string? LatestOrderId, bool IsTestPurchase);
public sealed record ExportDevice(string? DeviceName, DateTimeOffset SignedInAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
