using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;

namespace Trazo.Infrastructure.Planning;

public interface ITrainingPlanService
{
    Task<PlanOptions> GetOptionsAsync(CancellationToken ct);
    Task<PlanResult<PlanView>> CreateAsync(Guid userId, PlanProfile profile, int? seed, CancellationToken ct);
    Task<PlanView?> GetCurrentAsync(Guid userId, CancellationToken ct);
    Task<WeekView?> GetCurrentWeekAsync(Guid userId, CancellationToken ct);
    Task<PlanResult<LogResult>> LogSessionAsync(Guid userId, Guid sessionId, ParsedLog log, CancellationToken ct);
    Task<PlanResult<AlternativesResult>> GetAlternativesAsync(Guid userId, Guid sessionId, string exerciseId, int limit, CancellationToken ct);
    Task<PlanResult<SessionView>> SwapExerciseAsync(Guid userId, Guid sessionId, string exerciseId, string replacementId, bool applyToRemainingBlock, CancellationToken ct);
    Task<IReadOnlyList<HistoryItem>?> GetHistoryAsync(Guid userId, int take, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Orchestrates the planning engine against the database. A plan stores its profile and the macrocycle phases; the
/// sessions are generated one mesocycle at a time, when their first week is reached, so a future block can use what the
/// user really did and no stored data (or response) ever contains the whole macrocycle in detail. Every method takes the
/// user id and only ever touches that user's active plan.
/// </summary>
public sealed class TrainingPlanService(TrazoDbContext db, TimeProvider clock) : ITrainingPlanService
{
    public const string Disclaimer =
        "Plan generado automáticamente a partir de tu perfil. No es consejo médico ni sustituye a un profesional: " +
        "si notas dolor, detén el ejercicio y consulta a un especialista.";

    private const int RecentLogsForHistory = 60;
    private const int LoadReferenceLogs = 40;
    private const int MaximumFutureSessionsToAdapt = TrainingAutoAdaptationPolicy.MaximumFutureSessions;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // ---------------------------------------------------------------- options

    public async Task<PlanOptions> GetOptionsAsync(CancellationToken ct)
    {
        var equipment = (await db.ExerciseCatalog.AsNoTracking().Where(e => e.IsActive).Select(e => e.RequiredEquipment).ToListAsync(ct))
            .SelectMany(list => list).Select(PlanProfileParser.NormalizeToken).Where(token => token.Length > 0)
            .Distinct().OrderBy(token => token, StringComparer.Ordinal).ToArray();
        return new PlanOptions(
            Names<TrainingGoal>(), Names<TrainingExperience>(), Names<TrainingProgramSplit>(),
            Enum.GetNames<DayOfWeek>(), LimitationRules.Areas, equipment,
            2, 6, 35, 180, PlanProfileParser.MinimumTotalWeeks, PlanProfileParser.MaximumTotalWeeks, PlanProfileParser.DefaultTotalWeeks);
    }

    private static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();

    // ---------------------------------------------------------------- create

    public async Task<PlanResult<PlanView>> CreateAsync(Guid userId, PlanProfile profile, int? seed, CancellationToken ct)
    {
        if (await db.TrainingPlans.AnyAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct))
            return PlanResult<PlanView>.Fail(PlanFailure.ActivePlanExists,
                "Ya tienes un plan activo. Archívalo antes de crear otro.");

        var macrocycle = TrainingMacrocycleDraftBuilder.Build(new TrainingMacrocycleDraftRequest(
            profile.StartDate, profile.TotalWeeks, profile.Goal, profile.Experience, profile.IncludeDeloadPhases,
            profile.DeloadEveryTrainingWeeks));
        if (macrocycle.Draft is null)
            return PlanResult<PlanView>.Fail(PlanFailure.InvalidRequest, "No se pudo planificar el macrociclo.",
                new Dictionary<string, string[]> { ["plan"] = macrocycle.Errors.ToArray() });

        var now = clock.GetUtcNow();
        var planSeed = seed ?? RandomNumberGenerator.GetInt32(0, 1_000_000);
        var plan = new TrainingPlan
        {
            Id = Guid.NewGuid(), UserId = userId, Status = TrainingPlanStatus.Active,
            Goal = profile.Goal.ToString(), Experience = profile.Experience.ToString(), Split = profile.Split.ToString(),
            StartDate = profile.StartDate, TotalWeeks = profile.TotalWeeks,
            TrainingDays = profile.TrainingDays.Select(d => d.ToString()).ToArray(),
            MaxSessionMinutes = profile.MaxSessionMinutes, AvailableEquipment = profile.AvailableEquipment.ToArray(),
            ExcludedExerciseIds = profile.ExcludedExerciseIds.ToArray(), Limitations = profile.Limitations.ToArray(),
            IncludeDeloadPhases = profile.IncludeDeloadPhases, DeloadEveryTrainingWeeks = profile.DeloadEveryTrainingWeeks,
            Seed = planSeed, EngineVersion = string.Empty, MacrocycleName = macrocycle.Draft.Name,
            MacrocycleNotes = macrocycle.Draft.Notes, CreatedAt = now, UpdatedAt = now
        };
        foreach (var phase in macrocycle.Draft.Phases)
            plan.Phases.Add(new TrainingPlanPhase
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, SequenceNumber = phase.SequenceNumber, Name = phase.Name,
                PhaseType = phase.PhaseType, StartDate = phase.StartDate, EndDate = phase.EndDate, Objective = phase.Objective
            });

        // Generate the first block now: if the profile cannot be satisfied (no exercise for a movement pattern, for
        // example) the user hears it immediately and nothing is stored.
        var candidates = await LoadCandidatesAsync(ct);
        var firstSlot = PlanSchedule.Slots(macrocycle.Draft).First(slot => slot.Contains(1));
        var loads = await LoadReferencesAsync(userId, ct);
        var generation = MesocycleGenerator.Generate(profile, planSeed, firstSlot, candidates.Candidates, null, false, loads);
        if (!generation.Succeeded)
            return PlanResult<PlanView>.Fail(PlanFailure.GenerationFailed,
                "No se pudo generar un plan con ese perfil.", new Dictionary<string, string[]> { ["plan"] = generation.Errors.ToArray() });
        plan.EngineVersion = generation.EngineVersion;
        AddMesocycle(plan, firstSlot, generation, now);

        db.TrainingPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            // The partial unique index stopped a concurrent creation; any other failure is a real error.
            if (!await db.TrainingPlans.AsNoTracking().AnyAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct)) throw;
            return PlanResult<PlanView>.Fail(PlanFailure.ActivePlanExists, "Ya tienes un plan activo. Archívalo antes de crear otro.");
        }
        return PlanResult<PlanView>.Ok(ToView(plan));
    }

    // ---------------------------------------------------------------- read

    public async Task<PlanView?> GetCurrentAsync(Guid userId, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        return plan is null ? null : ToView(plan);
    }

    public async Task<WeekView?> GetCurrentWeekAsync(Guid userId, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var position = PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        if (position.State == PlanTimeline.Finished)
        {
            plan.Status = TrainingPlanStatus.Completed;
            plan.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new WeekView(plan.Id, position.Week, PlanSchedule.WeekStart(plan.StartDate, position.Week),
                PlanSchedule.WeekStart(plan.StartDate, position.Week).AddDays(6), nameof(PlanTimeline.Finished), null, []);
        }
        return await BuildWeekAsync(plan, position, ct);
    }

    public async Task<IReadOnlyList<HistoryItem>?> GetHistoryAsync(Guid userId, int take, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.PlanId == plan.Id)
            .OrderByDescending(l => l.LoggedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var ids = logs.Select(l => l.PlannedSessionId).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return logs.Select(log =>
        {
            var session = sessions[log.PlannedSessionId];
            var planned = PrescriptionJson.Read(session.PrescriptionJson).Sum(e => e.Sets);
            return new HistoryItem(session.Id, session.WeekNumber, session.Date, session.Title, log.Outcome.ToString(),
                log.LoggedAt, log.Sets.Count(s => s.Completed), planned);
        }).ToList();
    }

    public async Task<bool> ArchiveAsync(Guid userId, CancellationToken ct)
    {
        var plan = await db.TrainingPlans.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct);
        if (plan is null) return false;
        plan.Status = TrainingPlanStatus.Archived;
        plan.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------------------------------------------------------- log + adapt

    public async Task<PlanResult<LogResult>> LogSessionAsync(Guid userId, Guid sessionId, ParsedLog log, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<LogResult>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        var session = await db.PlannedSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlanId == plan.Id, ct);
        if (session is null) return PlanResult<LogResult>.Fail(PlanFailure.SessionNotFound, "La sesión no existe.");
        var window = WindowCheck(plan, session);
        if (window is not null) return PlanResult<LogResult>.Fail(PlanFailure.SessionOutOfWindow, window);
        if (session.Status != PlannedSessionStatus.Planned || await db.WorkoutLogs.AnyAsync(l => l.PlannedSessionId == session.Id, ct))
            return PlanResult<LogResult>.Fail(PlanFailure.AlreadyLogged, "Esta sesión ya está registrada.");

        var prescribed = PrescriptionJson.Read(session.PrescriptionJson);
        var errors = new Dictionary<string, string[]>();
        foreach (var set in log.Sets)
        {
            var exercise = prescribed.FirstOrDefault(e => string.Equals(e.ExerciseId, set.ExerciseId, StringComparison.OrdinalIgnoreCase));
            if (exercise is null) errors[$"sets.{set.ExerciseId}"] = ["El ejercicio no pertenece a esta sesión."];
            else if (set.SetNumber > Math.Max(exercise.Sets + 2, exercise.Sets)) errors[$"sets.{set.ExerciseId}"] = [$"Esta sesión prescribe {exercise.Sets} series de este ejercicio."];
        }
        if (errors.Count > 0)
            return PlanResult<LogResult>.Fail(PlanFailure.ExerciseNotInSession, "El registro contiene series que no corresponden a la sesión.", errors);

        var now = clock.GetUtcNow();
        var workout = new WorkoutLog
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, PlannedSessionId = session.Id, LoggedAt = now,
            Outcome = log.Outcome, NonCompletionReason = log.NonCompletionReason, Recovery = log.Recovery,
            SessionRpe = log.SessionRpe, PainReported = log.Pain, Notes = log.Notes
        };
        foreach (var set in log.Sets)
            workout.Sets.Add(new WorkoutSetLog
            {
                Id = Guid.NewGuid(), WorkoutLogId = workout.Id, ExerciseId = prescribed.First(e => string.Equals(e.ExerciseId, set.ExerciseId, StringComparison.OrdinalIgnoreCase)).ExerciseId,
                SetNumber = set.SetNumber, WeightKg = set.WeightKg, Repetitions = set.Repetitions, Rir = set.Rir,
                Completed = set.Completed, SkippedReason = set.SkippedReason, PainReported = set.Pain
            });
        db.WorkoutLogs.Add(workout);
        session.Status = log.Outcome switch
        {
            WorkoutOutcome.Completed => PlannedSessionStatus.Completed,
            WorkoutOutcome.Partial => PlannedSessionStatus.Partial,
            _ => PlannedSessionStatus.Skipped
        };
        session.UpdatedAt = now;

        var report = await AdaptAsync(plan, session, workout, prescribed, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.WorkoutLogs.AsNoTracking().AnyAsync(l => l.PlannedSessionId == session.Id, ct)) throw;
            return PlanResult<LogResult>.Fail(PlanFailure.AlreadyLogged, "Esta sesión ya está registrada.");
        }
        return PlanResult<LogResult>.Ok(new LogResult(ToSessionView(session, workout), report));
    }

    /// <summary>Reads the whole history of the plan, asks the planner what to change and applies it to the upcoming sessions.</summary>
    private async Task<AdaptationReport> AdaptAsync(
        TrainingPlan plan, PlannedSession logged, WorkoutLog workout, IReadOnlyList<TrainingExercisePrescription> prescribed,
        DateTimeOffset now, CancellationToken ct)
    {
        var mesocycles = await db.TrainingMesocycles.AsNoTracking().Where(m => m.PlanId == plan.Id).ToDictionaryAsync(m => m.Id, ct);
        var pastLogs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets)
            .Where(l => l.PlanId == plan.Id && l.Id != workout.Id).OrderByDescending(l => l.LoggedAt).Take(RecentLogsForHistory)
            .ToListAsync(ct);
        var logs = pastLogs.OrderBy(l => l.LoggedAt).Append(workout).ToList();
        var sessionIds = logs.Select(l => l.PlannedSessionId).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        sessions[logged.Id] = logged;

        var exposures = new List<ExerciseExposureRecord>();
        var sessionRecords = new List<SessionRecord>();
        foreach (var log in logs)
        {
            if (!sessions.TryGetValue(log.PlannedSessionId, out var session)) continue;
            var exercises = PrescriptionJson.Read(session.PrescriptionJson);
            var original = PrescriptionJson.OriginalSets(session.PrescriptionJson);
            var eligible = !(mesocycles.TryGetValue(session.MesocycleId, out var meso) && meso.IsDeload)
                           && !PrescriptionJson.IsAdaptationDeload(session.PrescriptionJson);
            foreach (var exercise in exercises)
            {
                var sets = log.Sets.Where(s => s.ExerciseId == exercise.ExerciseId).OrderBy(s => s.SetNumber)
                    .Select(s => new SetRecord(s.WeightKg, s.Repetitions, s.Rir, s.Completed, s.SkippedReason, s.PainReported)).ToArray();
                exposures.Add(new ExerciseExposureRecord(
                    log.LoggedAt, exercise.ExerciseId, exercise.Sets, exercise.MinRepetitions, exercise.MaxRepetitions,
                    exercise.TargetRepetitionsInReserve, exercise.ProgressionRule, eligible, sets, log.Outcome, log.Recovery,
                    original.TryGetValue(exercise.ExerciseId, out var dose) ? dose : null));
            }
            var plannedSets = exercises.Sum(e => e.Sets);
            sessionRecords.Add(new SessionRecord(log.LoggedAt, log.Outcome, log.NonCompletionReason, log.Recovery, log.PainReported,
                log.SessionRpe, plannedSets > 0 ? Math.Min(1m, (decimal)log.Sets.Count(s => s.Completed) / plannedSets) : null));
        }

        var recent = (await db.PlanAdaptations.AsNoTracking().Where(a => a.PlanId == plan.Id && a.AppliedAt > now.AddDays(-30)).ToListAsync(ct))
            .Select(a => new TrainingAutoAdaptationRecord(KindOf(a.Kind), a.AppliedAt)).ToList();

        var outcome = AdaptationPlanner.Evaluate(new AdaptationInput(
            exposures, sessionRecords, prescribed.Select(e => e.ExerciseId).ToArray(), recent, now));

        // Upcoming sessions that can still change: planned, not logged, after this one.
        var upcoming = await db.PlannedSessions
            .Where(s => s.PlanId == plan.Id && s.Status == PlannedSessionStatus.Planned && s.Date > logged.Date)
            .OrderBy(s => s.Date).Take(40).ToListAsync(ct);
        var applied = new List<AppliedAdaptationView>();

        foreach (var action in outcome.Actions)
        {
            var targets = action.ExerciseIds.ToHashSet(StringComparer.Ordinal);
            var changed = 0;
            IEnumerable<PlannedSession> window = upcoming;
            if (action.Kind == TrainingAutoAdaptationKind.Deload)
                window = upcoming.Where(s => s.Date <= Today.AddDays(TrainingAutoAdaptationPolicy.DeloadWindowDays));
            else if (action.Kind != TrainingAutoAdaptationKind.HoldProgression)
                window = upcoming.Take(MaximumFutureSessionsToAdapt);
            foreach (var future in window)
            {
                var json = future.PrescriptionJson;
                var ok = action.Kind switch
                {
                    TrainingAutoAdaptationKind.ReduceSets => TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json, targets, out var updated, out _) && Set(out json, updated),
                    TrainingAutoAdaptationKind.RaiseReserve => TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var updated, out _) && Set(out json, updated),
                    TrainingAutoAdaptationKind.RestoreSets => TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(json, targets, out var updated, out _, out _) && Set(out json, updated),
                    TrainingAutoAdaptationKind.Deload => TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(json, out var updated, out _) && Set(out json, updated),
                    _ => false
                };
                if (!ok) continue;
                future.PrescriptionJson = json;
                future.UpdatedAt = now;
                changed++;
            }

            db.PlanAdaptations.Add(new PlanAdaptation
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, WorkoutLogId = workout.Id,
                Kind = TrainingAutoAdaptationPolicy.DescribeKind(action.Kind), ExerciseIds = action.ExerciseIds.ToArray(),
                SessionsChanged = changed, Explanation = action.Explanation, AppliedAt = now
            });
            applied.Add(new AppliedAdaptationView(TrainingAutoAdaptationPolicy.DescribeKind(action.Kind), action.ExerciseIds, changed, action.Explanation));
        }

        // Carry the load of the next exposure into the next upcoming session of each exercise (never into a deload block).
        foreach (var advice in outcome.Exercises.Where(a => a.NextLoadKg is > 0))
        {
            var next = upcoming.FirstOrDefault(s => PrescriptionJson.Read(s.PrescriptionJson).Any(e => e.ExerciseId == advice.ExerciseId));
            if (next is null) continue;
            if ((mesocycles.TryGetValue(next.MesocycleId, out var meso) && meso.IsDeload) || PrescriptionJson.IsAdaptationDeload(next.PrescriptionJson)) continue;
            if (TrainingAdaptationPrescriptionAdjuster.TrySetLoad(next.PrescriptionJson, advice.ExerciseId, advice.NextLoadKg!.Value,
                    $"Según tu último registro: {advice.Explanation}", out var updated, out _))
            {
                next.PrescriptionJson = updated;
                next.UpdatedAt = now;
            }
        }

        return new AdaptationReport(
            outcome.Exercises.Select(a => new ExerciseAdviceView(a.ExerciseId, a.Action, a.Explanation, a.NextLoadKg, a.SuggestedRepetitions)).ToList(),
            outcome.SessionAction, outcome.SessionExplanation, applied, outcome.Notes);

        static bool Set(out string target, string value) { target = value; return true; }
    }

    private static TrainingAutoAdaptationKind KindOf(string name) =>
        Enum.GetValues<TrainingAutoAdaptationKind>().FirstOrDefault(k => TrainingAutoAdaptationPolicy.DescribeKind(k) == name);

    // ---------------------------------------------------------------- alternatives and swaps

    public async Task<PlanResult<AlternativesResult>> GetAlternativesAsync(
        Guid userId, Guid sessionId, string exerciseId, int limit, CancellationToken ct)
    {
        var context = await SessionContextAsync(userId, sessionId, exerciseId, ct);
        if (context.Failure is not null) return PlanResult<AlternativesResult>.Fail(context.Failure.Value, context.Message!);
        var alternatives = FindAlternatives(context, limit);
        return PlanResult<AlternativesResult>.Ok(new AlternativesResult(
            exerciseId,
            alternatives.Alternatives.Select(a => new AlternativeView(
                a.ExerciseId, a.Name, a.MovementPattern, a.PrimaryMuscleGroups, a.RequiredEquipment, a.MatchLevel)).ToList(),
            alternatives.Message));
    }

    public async Task<PlanResult<SessionView>> SwapExerciseAsync(
        Guid userId, Guid sessionId, string exerciseId, string replacementId, bool applyToRemainingBlock, CancellationToken ct)
    {
        var context = await SessionContextAsync(userId, sessionId, exerciseId, ct);
        if (context.Failure is not null) return PlanResult<SessionView>.Fail(context.Failure.Value, context.Message!);
        if (context.Session!.Status != PlannedSessionStatus.Planned)
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Solo se pueden cambiar ejercicios de sesiones pendientes.");

        var replacement = FindAlternatives(context, 10).Alternatives
            .FirstOrDefault(a => string.Equals(a.ExerciseId, replacementId, StringComparison.OrdinalIgnoreCase));
        var candidate = replacement is null ? null
            : context.Candidates!.FirstOrDefault(c => string.Equals(c.ExerciseId, replacement.ExerciseId, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Ese ejercicio no es una alternativa válida para tu perfil.");

        var now = clock.GetUtcNow();
        var targets = new List<PlannedSession> { context.Session };
        if (applyToRemainingBlock)
            targets.AddRange(await db.PlannedSessions.Where(s => s.PlanId == context.Plan!.Id && s.MesocycleId == context.Session.MesocycleId
                && s.Status == PlannedSessionStatus.Planned && s.Date > context.Session.Date).ToListAsync(ct));
        var primarySwapped = false;
        foreach (var target in targets)
        {
            if (!TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(target.PrescriptionJson, exerciseId, candidate, out var updated, out _)) continue;
            target.PrescriptionJson = updated;
            target.UpdatedAt = now;
            primarySwapped |= ReferenceEquals(target, context.Session);
        }
        if (!primarySwapped)
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "No se pudo cambiar el ejercicio: ya está en la sesión o no existe.");
        await db.SaveChangesAsync(ct);
        return PlanResult<SessionView>.Ok(ToSessionView(context.Session, null));
    }

    private sealed record SessionContext(
        PlanFailure? Failure, string? Message, TrainingPlan? Plan, PlannedSession? Session,
        TrainingExercisePrescription? Exercise, IReadOnlyList<TrainingExerciseCandidate>? Candidates, IReadOnlyList<TrainingExercisePrescription>? Prescribed);

    private async Task<SessionContext> SessionContextAsync(Guid userId, Guid sessionId, string exerciseId, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return new(PlanFailure.NoActivePlan, "No tienes un plan activo.", null, null, null, null, null);
        var session = await db.PlannedSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlanId == plan.Id, ct);
        if (session is null) return new(PlanFailure.SessionNotFound, "La sesión no existe.", null, null, null, null, null);
        var window = WindowCheck(plan, session);
        if (window is not null) return new(PlanFailure.SessionOutOfWindow, window, null, null, null, null, null);
        var prescribed = PrescriptionJson.Read(session.PrescriptionJson);
        var exercise = prescribed.FirstOrDefault(e => string.Equals(e.ExerciseId, exerciseId, StringComparison.OrdinalIgnoreCase));
        if (exercise is null) return new(PlanFailure.ExerciseNotInSession, "El ejercicio no pertenece a esta sesión.", null, null, null, null, null);
        var candidates = (await LoadCandidatesAsync(ct)).Candidates;
        return new(null, null, plan, session, exercise, candidates, prescribed);
    }

    private static TrainingAlternativeResult FindAlternatives(SessionContext context, int limit)
    {
        var plan = context.Plan!;
        var excluded = plan.ExcludedExerciseIds
            .Concat(LimitationRules.ExcludedExerciseIds(plan.Limitations, context.Candidates!))
            .Concat(context.Prescribed!.Select(e => e.ExerciseId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TrainingExerciseAlternativeFinder.Find(
            context.Exercise!.ExerciseId, context.Exercise.PrimaryMuscleGroups, context.Candidates!, excluded,
            Enum.Parse<TrainingGoal>(plan.Goal), Enum.Parse<TrainingExperience>(plan.Experience), plan.AvailableEquipment,
            Math.Clamp(limit, 1, 10));
    }

    // ---------------------------------------------------------------- generation of later blocks

    private async Task<TrainingPlan?> ActivePlanAsync(Guid userId, CancellationToken ct) =>
        await db.TrainingPlans.Include(p => p.Phases).Include(p => p.Mesocycles)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct);

    private async Task<WeekView> BuildWeekAsync(TrainingPlan plan, PlanPosition position, CancellationToken ct)
    {
        var slots = PlanSchedule.Slots(plan.StartDate, plan.Phases.Select(p => new PhaseSpan(p.SequenceNumber, p.PhaseType, p.StartDate, p.EndDate)));
        var slot = slots.First(s => s.Contains(position.Week));
        var mesocycle = plan.Mesocycles.FirstOrDefault(m => m.Sequence == slot.Sequence);
        if (mesocycle is null)
        {
            mesocycle = await GenerateMesocycleAsync(plan, slot, ct);
            if (mesocycle is null)
                return new WeekView(plan.Id, position.Week, PlanSchedule.WeekStart(plan.StartDate, position.Week),
                    PlanSchedule.WeekStart(plan.StartDate, position.Week).AddDays(6), "GenerationFailed", null, []);
        }

        var sessions = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.WeekNumber == position.Week).OrderBy(s => s.Date).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => ids.Contains(l.PlannedSessionId)).ToListAsync(ct);
        var weekStart = PlanSchedule.WeekStart(plan.StartDate, position.Week);
        return new WeekView(
            plan.Id, position.Week, weekStart, weekStart.AddDays(6), position.State.ToString(),
            new MesocycleView(mesocycle.Sequence, mesocycle.PhaseType, mesocycle.IsDeload, mesocycle.StartWeek, mesocycle.Weeks,
                position.Week - mesocycle.StartWeek + 1),
            sessions.Select(s => ToSessionView(s, logs.FirstOrDefault(l => l.PlannedSessionId == s.Id))).ToList());
    }

    /// <summary>Generates the block of the given slot from the catalog and what the user has done so far.</summary>
    private async Task<TrainingMesocycle?> GenerateMesocycleAsync(TrainingPlan plan, MesocycleSlot slot, CancellationToken ct)
    {
        var candidates = (await LoadCandidatesAsync(ct)).Candidates;
        var previous = plan.Mesocycles.Where(m => m.Sequence < slot.Sequence).OrderByDescending(m => m.Sequence).FirstOrDefault();
        IReadOnlyList<string>? previousIds = null;
        if (previous is not null)
        {
            var lastWeek = await db.PlannedSessions.AsNoTracking().Where(s => s.MesocycleId == previous.Id)
                .OrderByDescending(s => s.WeekNumber).Select(s => s.WeekNumber).FirstOrDefaultAsync(ct);
            previousIds = (await db.PlannedSessions.AsNoTracking().Where(s => s.MesocycleId == previous.Id && s.WeekNumber == lastWeek)
                    .Select(s => s.PrescriptionJson).ToListAsync(ct))
                .SelectMany(json => PrescriptionJson.Read(json).Select(e => e.ExerciseId)).Distinct().ToArray();
        }

        var profile = ProfileOf(plan);
        var generation = MesocycleGenerator.Generate(
            profile, plan.Seed, slot, candidates, previousIds,
            continuesPreviousBlock: previous is { IsDeload: false } && previous.Sequence == slot.Sequence - 1,
            loadReferences: await LoadReferencesAsync(plan.UserId, ct));
        if (!generation.Succeeded) return null;

        var mesocycle = AddMesocycle(plan, slot, generation, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request generated the same block first: use theirs.
            db.ChangeTracker.Clear();
            return await db.TrainingMesocycles.AsNoTracking().FirstOrDefaultAsync(m => m.PlanId == plan.Id && m.Sequence == slot.Sequence, ct);
        }
        return mesocycle;
    }

    private static TrainingMesocycle AddMesocycle(TrainingPlan plan, MesocycleSlot slot, MesocycleGeneration generation, DateTimeOffset now)
    {
        var mesocycle = new TrainingMesocycle
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Sequence = slot.Sequence, PhaseSequenceNumber = slot.PhaseSequenceNumber,
            PhaseType = slot.PhaseType, IsDeload = slot.IsDeload, StartWeek = slot.StartWeek, Weeks = slot.Weeks, CreatedAt = now
        };
        foreach (var session in generation.Sessions)
            mesocycle.Sessions.Add(new PlannedSession
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = mesocycle.Id, WeekNumber = session.WeekNumber, Date = session.Date,
                Title = session.Title, EstimatedDurationMinutes = session.EstimatedDurationMinutes,
                PrescriptionJson = session.PrescriptionJson, Status = PlannedSessionStatus.Planned, UpdatedAt = now
            });
        plan.Mesocycles.Add(mesocycle);
        return mesocycle;
    }

    private static PlanProfile ProfileOf(TrainingPlan plan) => new(
        Enum.Parse<TrainingGoal>(plan.Goal), Enum.Parse<TrainingExperience>(plan.Experience), plan.StartDate, plan.TotalWeeks,
        plan.TrainingDays.Select(Enum.Parse<DayOfWeek>).ToList(), plan.MaxSessionMinutes, plan.AvailableEquipment,
        plan.ExcludedExerciseIds, plan.Limitations, Enum.Parse<TrainingProgramSplit>(plan.Split), plan.IncludeDeloadPhases,
        plan.DeloadEveryTrainingWeeks);

    /// <summary>The most recent completed performance of each exercise, so a new block starts from real loads.</summary>
    private async Task<IReadOnlyList<TrainingLoadReference>> LoadReferencesAsync(Guid userId, CancellationToken ct)
    {
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.UserId == userId)
            .OrderByDescending(l => l.LoggedAt).Take(LoadReferenceLogs).ToListAsync(ct);
        var references = new List<TrainingLoadReference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var log in logs)
            foreach (var group in log.Sets.Where(s => s.Completed && s.WeightKg is > 0 && s.Repetitions is > 0).GroupBy(s => s.ExerciseId))
            {
                if (!seen.Add(group.Key)) continue;
                var top = group.OrderByDescending(s => s.WeightKg).ThenBy(s => s.SetNumber).First();
                references.Add(new TrainingLoadReference(group.Key, top.WeightKg!.Value, top.Repetitions!.Value, top.Rir, group.Count()));
            }
        return references;
    }

    private async Task<CandidateMappingResult> LoadCandidatesAsync(CancellationToken ct) =>
        ExerciseCandidateMapper.Map(await db.ExerciseCatalog.AsNoTracking().ToListAsync(ct));

    /// <summary>Sessions can be used in the current week and the one before (to log a late workout).</summary>
    private string? WindowCheck(TrainingPlan plan, PlannedSession session)
    {
        var position = PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        if (position.State == PlanTimeline.Finished) return "El plan ha terminado.";
        return session.WeekNumber == position.Week || session.WeekNumber == position.Week - 1
            ? null
            : "Solo puedes usar las sesiones de la semana en curso.";
    }

    // ---------------------------------------------------------------- views

    private PlanView ToView(TrainingPlan plan)
    {
        var position = PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        var today = Today;
        var phase = plan.Phases.OrderBy(p => p.SequenceNumber)
            .FirstOrDefault(p => today >= p.StartDate && today <= p.EndDate)
            ?? (position.State == PlanTimeline.NotStarted ? plan.Phases.MinBy(p => p.SequenceNumber) : null);
        return new PlanView(
            plan.Id, plan.Status.ToString(), position.State.ToString(), position.Week, plan.TotalWeeks, plan.StartDate,
            plan.StartDate.AddDays(plan.TotalWeeks * 7 - 1), plan.Goal, plan.Experience, plan.Split, plan.TrainingDays,
            plan.MaxSessionMinutes, plan.AvailableEquipment, plan.ExcludedExerciseIds, plan.Limitations, phase?.SequenceNumber,
            new MacrocycleView(plan.MacrocycleName, plan.MacrocycleNotes, plan.Phases.OrderBy(p => p.SequenceNumber)
                .Select(p => new PhaseView(p.SequenceNumber, p.Name, p.PhaseType, p.StartDate, p.EndDate, p.Objective)).ToList()),
            Disclaimer);
    }

    private static SessionView ToSessionView(PlannedSession session, WorkoutLog? log) => new(
        session.Id, session.WeekNumber, session.Date, session.Date.DayOfWeek, session.Title, session.EstimatedDurationMinutes,
        session.Status.ToString(),
        PrescriptionJson.Read(session.PrescriptionJson).OrderBy(e => e.Order).Select(e => new ExerciseView(
            e.ExerciseId, e.ExerciseNameSnapshot, e.Order, e.Sets, e.MinRepetitions, e.MaxRepetitions, e.TargetRepetitionsInReserve,
            e.RestSeconds, e.ProgressionRule.ToString(), e.TargetLoadKg, e.LoadBasis, e.WarmupSets, e.WarmupNote,
            e.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), e.SecondaryMuscleGroups.Select(m => m.ToString()).ToList(),
            PrescriptionJson.SwappedFrom(session.PrescriptionJson, e.ExerciseId))).ToList(),
        log is null ? null : new LogView(
            log.Id, log.LoggedAt, log.Outcome.ToString(), log.NonCompletionReason, log.Recovery, log.SessionRpe, log.PainReported, log.Notes,
            log.Sets.OrderBy(s => s.ExerciseId, StringComparer.Ordinal).ThenBy(s => s.SetNumber)
                .Select(s => new LoggedSetView(s.ExerciseId, s.SetNumber, s.WeightKg, s.Repetitions, s.Rir, s.Completed, s.SkippedReason, s.PainReported)).ToList()));
}
