using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Trazo.Application.Localization;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Billing;
using Trazo.Training;

namespace Trazo.Infrastructure.Planning;

public interface ITrainingPlanService
{
    Task<PlanOptions> GetOptionsAsync(CancellationToken ct);
    /// <summary>The current date in the time zone of the user (request header, profile or Europe/Madrid).</summary>
    Task<DateOnly> TodayAsync(Guid userId, CancellationToken ct);
    Task<PlanResult<PlanView>> CreateAsync(Guid userId, PlanProfile profile, int? seed, CancellationToken ct);
    /// <summary>Creates a plan written by the user (free): their routines repeated every week, no engine involved.</summary>
    Task<PlanResult<PlanView>> CreateManualAsync(Guid userId, ManualPlan manual, CancellationToken ct);
    Task<PlanResult<PlanView>> UpdateManualAsync(Guid userId, ManualPlan manual, CancellationToken ct);
    Task<PlanView?> GetCurrentAsync(Guid userId, CancellationToken ct);
    Task<WeekView?> GetCurrentWeekAsync(Guid userId, CancellationToken ct);
    Task<PlanResult<LogResult>> LogSessionAsync(Guid userId, Guid sessionId, ParsedLog log, CancellationToken ct);
    Task<PlanResult<AlternativesResult>> GetAlternativesAsync(Guid userId, Guid sessionId, string exerciseId, int limit, CancellationToken ct);
    Task<PlanResult<SessionView>> AddExerciseAsync(Guid userId, Guid sessionId, AddExerciseRequest request, CancellationToken ct);
    Task<PlanResult<SessionView>> SwapExerciseAsync(Guid userId, Guid sessionId, string exerciseId, string replacementId, bool applyToRemainingBlock, CancellationToken ct);
    Task<BlockView?> GetCurrentBlockAsync(Guid userId, CancellationToken ct);
    /// <summary>The session to do next (by program order, whatever the date), with where it sits and whether the muscles are recovered.</summary>
    Task<NextSessionView?> GetNextAsync(Guid userId, CancellationToken ct);
    /// <summary>Moves the pending program of a calendar plan forward, by whole weeks, so its first pending week is the current one.</summary>
    Task<PlanResult<RescheduleResult>> RescheduleAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<HistoryItem>?> GetHistoryAsync(Guid userId, int take, CancellationToken ct);
    Task<PlanLoadView?> GetLoadAsync(Guid userId, CancellationToken ct);
    Task<ProgressView> GetProgressAsync(Guid userId, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Orchestrates the planning engine against the database. A plan stores its profile and the macrocycle phases; the
/// sessions are generated one mesocycle at a time, when their first week is reached, so a future block can use what the
/// user really did and no stored data (or response) ever contains the whole macrocycle in detail. Every method takes the
/// user id and only ever touches that user's active plan.
/// </summary>
public sealed class TrainingPlanService(TrazoDbContext db, TimeProvider clock, IUserTimeZone zones, ISubscriptionService subscriptions, RequestLanguage language) : ITrainingPlanService
{
    public const string LockedMessage = "Este plan se generó automáticamente: para usarlo necesitas Premium.";
    public const string AdaptationLockedMessage = "La adaptación automática es una función Premium.";

    public const string Disclaimer =
        "Plan generado automáticamente a partir de tu perfil. No es consejo médico ni sustituye a un profesional: " +
        "si notas dolor, detén el ejercicio y consulta a un especialista.";

    public const string ManualDisclaimer =
        "Plan creado por ti. Con Premium, TRAZO adapta cargas, series y descansos según cómo te va. " +
        "No es consejo médico: si notas dolor, detén el ejercicio y consulta a un especialista.";

    private const int FutureToleranceMinutes = 5;
    private const int RecentLogsForHistory = 60;
    private const int LoadReferenceLogs = 40;
    private const int MaximumFutureSessionsToAdapt = TrainingAutoAdaptationPolicy.MaximumFutureSessions;

    // "Today" follows the user's time zone, not the server's. It is resolved once per request by the methods that take a
    // user id (UseUserClockAsync); a service that has not resolved it yet falls back to UTC.
    private DateOnly? _today;
    private DateOnly Today => _today ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private async Task UseUserClockAsync(Guid userId, CancellationToken ct)
    {
        var zone = await zones.ResolveAsync(userId, ct);
        _today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        // Exercise names are stored in Spanish in the prescription; in English they are read from the catalog, once per request.
        if (Lang == AppLanguage.En && _namesEn is null)
            _namesEn = (await db.ExerciseCatalog.AsNoTracking().Select(e => new { e.Id, e.NameEn }).ToListAsync(ct))
                .Where(e => !string.IsNullOrWhiteSpace(e.NameEn)).ToDictionary(e => e.Id, e => e.NameEn, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- language

    private AppLanguage Lang => language.Current;
    private Dictionary<string, string>? _namesEn;
    /// <summary>Titles, phases and objectives of a GENERATED plan are the engine's texts; in a manual plan they are the user's own words.</summary>
    private bool _generatedPlan = true;

    private string T(string text) => Localizer.Translate(text, Lang) ?? text;
    private string? Tn(string? text) => Localizer.Translate(text, Lang);
    private string TitleOf(string title) => _generatedPlan ? T(title) : title;
    private string NameOf(string exerciseId, string fallback) =>
        Lang == AppLanguage.En && _namesEn is not null && _namesEn.TryGetValue(exerciseId, out var name) ? name : fallback;

    private AdaptationReport Localized(AdaptationReport r) => Lang == AppLanguage.Es ? r : r with
    {
        Exercises = r.Exercises.Select(e => e with { Explanation = T(e.Explanation) }).ToList(),
        SessionExplanation = T(r.SessionExplanation),
        Applied = r.Applied.Select(a => a with { Explanation = T(a.Explanation) }).ToList(),
        Notes = r.Notes.Select(T).ToList()
    };

    public async Task<DateOnly> TodayAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        return Today;
    }

    // ---------------------------------------------------------------- options

    /// <summary>Weeks start on Monday (ISO 8601), the convention for the Spanish audience. The ids never change.</summary>
    private static readonly IReadOnlyList<DayOption> WeekDays =
    [
        new("Monday", 1, "Lunes", "L"), new("Tuesday", 2, "Martes", "M"), new("Wednesday", 3, "Miércoles", "X"),
        new("Thursday", 4, "Jueves", "J"), new("Friday", 5, "Viernes", "V"), new("Saturday", 6, "Sábado", "S"),
        new("Sunday", 7, "Domingo", "D")
    ];

    private static readonly IReadOnlyList<DayOption> WeekDaysEn =
    [
        new("Monday", 1, "Monday", "M"), new("Tuesday", 2, "Tuesday", "T"), new("Wednesday", 3, "Wednesday", "W"),
        new("Thursday", 4, "Thursday", "T"), new("Friday", 5, "Friday", "F"), new("Saturday", 6, "Saturday", "S"),
        new("Sunday", 7, "Sunday", "S")
    ];

    public async Task<PlanOptions> GetOptionsAsync(CancellationToken ct)
    {
        // RequiredEquipment is the single source of truth for the generator selector.
        // The legacy Equipment field is descriptive metadata and must never expose generic or stale options.
        var equipment = (await db.ExerciseCatalog.AsNoTracking().Where(e => e.IsActive).Select(e => e.RequiredEquipment).ToListAsync(ct))
            .SelectMany(list => list).Select(PlanProfileParser.NormalizeToken).Where(token => token.Length > 0)
            .Where(token => !string.Equals(token, "machine", StringComparison.OrdinalIgnoreCase))
            .Distinct().OrderBy(token => token, StringComparer.Ordinal).ToArray();
        return new PlanOptions(
            Names<TrainingGoal>(), Names<TrainingExperience>(), Names<TrainingProgramSplit>(),
            WeekDays.Select(d => d.Id).ToArray(), LimitationRules.Areas, equipment,
            2, 6, 35, 180, PlanProfileParser.MinimumTotalWeeks, PlanProfileParser.MaximumTotalWeeks, PlanProfileParser.DefaultTotalWeeks,
            Lang == AppLanguage.En ? WeekDaysEn : WeekDays, nameof(DayOfWeek.Monday), UserTimeZone.DefaultId, Names<PlanSchedulingMode>(),
            PlanProfileParser.MinimumSessionsPerPeriod, PlanProfileParser.MaximumSessionsPerPeriod,
            Enum.GetValues<TrainingProgramSplit>().Select(split => new SplitSupportOption(
                split.ToString(),
                Enumerable.Range(2, 5).Where(days => TrainingSessionRotation.IsSupportedWeekly(split, days)).ToArray(),
                Enumerable.Range(TrainingSessionRotation.MinimumSessionsPerPeriod, TrainingSessionRotation.MaximumSessionsPerPeriod)
                    .Where(sessions => TrainingSessionRotation.IsSupportedSequence(split, sessions)).ToArray())).ToArray());
    }

    private static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();

    // ---------------------------------------------------------------- create

    public async Task<PlanResult<PlanView>> CreateAsync(Guid userId, PlanProfile profile, int? seed, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        if (await db.TrainingPlans.AnyAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct))
            return PlanResult<PlanView>.Fail(PlanFailure.ActivePlanExists,
                "Ya tienes un plan activo. Archívalo antes de crear otro.");

        var macrocycle = MacrocyclePlanner.Build(profile);
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
            SchedulingMode = profile.SchedulingMode, SessionsPerPeriod = profile.SessionsPerPeriod,
            // A sequence plan has no days of the week: its sessions are a continuous rotation identified by their order.
            TrainingDays = profile.SchedulingMode == PlanSchedulingMode.Sequence ? [] : profile.TrainingDays.Select(d => d.ToString()).ToArray(),
            MaxSessionMinutes = profile.MaxSessionMinutes, AvailableEquipment = profile.AvailableEquipment.ToArray(),
            ExcludedExerciseIds = profile.ExcludedExerciseIds.ToArray(), Limitations = profile.Limitations.ToArray(),
            IncludeDeloadPhases = profile.IncludeDeloadPhases, DeloadEveryTrainingWeeks = profile.DeloadEveryTrainingWeeks,
            Seed = planSeed, EngineVersion = string.Empty, MacrocycleName = macrocycle.Draft.Name,
            MacrocycleNotes = macrocycle.Draft.Notes, CreatedAt = now, UpdatedAt = now
        };
        var calendar = profile.SchedulingMode == PlanSchedulingMode.Calendar;
        foreach (var phase in macrocycle.Draft.Phases)
            plan.Phases.Add(new TrainingPlanPhase
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, SequenceNumber = phase.SequenceNumber, Name = phase.Name,
                PhaseType = phase.PhaseType, Objective = phase.Objective,
                StartPeriod = (phase.StartDate.DayNumber - profile.StartDate.DayNumber) / 7 + 1,
                EndPeriod = (phase.EndDate.DayNumber - profile.StartDate.DayNumber) / 7 + 1,
                // The structure is the same in both modes (periods); the dates only make sense on a calendar.
                StartDate = calendar ? phase.StartDate : null, EndDate = calendar ? phase.EndDate : null
            });

        // Generate the first block now: if the profile cannot be satisfied (no exercise for a movement pattern, for
        // example) the user hears it immediately and nothing is stored.
        var candidates = await LoadCandidatesAsync(ct);
        var firstSlot = SlotsOf(plan).First(slot => slot.Contains(1));
        var loads = await LoadReferencesAsync(userId, ct);
        var generation = MesocycleGenerator.Generate(profile, planSeed, firstSlot, candidates.Candidates, null, false, loads);
        if (!generation.Succeeded)
            return PlanResult<PlanView>.Fail(PlanFailure.GenerationFailed,
                "No se pudo generar un plan con ese perfil.", new Dictionary<string, string[]> { ["plan"] = generation.Errors.ToArray() });
        plan.EngineVersion = generation.EngineVersion;
        AddMesocycle(plan, firstSlot, generation, now, 1);

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
        return PlanResult<PlanView>.Ok(ToView(plan, calendar ? PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today) : new PlanPosition(PlanTimeline.InProgress, 1)));
    }

    public async Task<PlanResult<PlanView>> CreateManualAsync(Guid userId, ManualPlan manual, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        if (await db.TrainingPlans.AnyAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct))
            return PlanResult<PlanView>.Fail(PlanFailure.ActivePlanExists, "Ya tienes un plan activo. Archívalo antes de crear otro.");

        var catalog = (await LoadCandidatesAsync(ct)).Candidates.ToDictionary(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        for (var d = 0; d < manual.Days.Count; d++)
            for (var e = 0; e < manual.Days[d].Exercises.Count; e++)
                if (!catalog.ContainsKey(manual.Days[d].Exercises[e].ExerciseId))
                    errors[$"days.{d + 1}.exercises.{e + 1}"] = ["El ejercicio no existe en el catálogo."];
        if (errors.Count > 0)
            return PlanResult<PlanView>.Fail(PlanFailure.InvalidRequest, "Algún ejercicio no existe en el catálogo.", errors);

        var now = clock.GetUtcNow();
        var calendar = manual.SchedulingMode == PlanSchedulingMode.Calendar;
        var end = manual.StartDate.AddDays(manual.TotalWeeks * 7 - 1);
        var plan = new TrainingPlan
        {
            Id = Guid.NewGuid(), UserId = userId, Status = TrainingPlanStatus.Active, Origin = TrainingPlanOrigin.Manual,
            Goal = manual.Goal.ToString(), Experience = manual.Experience.ToString(), Split = TrainingProgramSplit.Automatic.ToString(),
            StartDate = manual.StartDate, TotalWeeks = manual.TotalWeeks, SchedulingMode = manual.SchedulingMode,
            SessionsPerPeriod = manual.Days.Count,
            TrainingDays = calendar ? manual.Days.Select(d => d.Day.ToString()).ToArray() : [],
            MaxSessionMinutes = 180, IncludeDeloadPhases = false, DeloadEveryTrainingWeeks = 4, Seed = 0, EngineVersion = "manual",
            MacrocycleName = manual.Name, MacrocycleNotes = manual.Notes, CreatedAt = now, UpdatedAt = now
        };
        plan.Phases.Add(new TrainingPlanPhase
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, SequenceNumber = 1, Name = manual.Name, PhaseType = "accumulation",
            StartPeriod = 1, EndPeriod = manual.TotalWeeks, StartDate = calendar ? manual.StartDate : null, EndDate = calendar ? end : null,
            Objective = manual.Notes.Length > 0 ? manual.Notes : "Plan manual"
        });

        var slots = SlotsOf(plan);
        var nextIndex = 1;
        foreach (var slot in slots)
        {
            var mesocycle = new TrainingMesocycle
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, Sequence = slot.Sequence, PhaseSequenceNumber = slot.PhaseSequenceNumber,
                PhaseType = slot.PhaseType, IsDeload = false, StartWeek = slot.StartWeek, Weeks = slot.Weeks, CreatedAt = now
            };
            for (var week = slot.StartWeek; week <= slot.EndWeek; week++)
                foreach (var day in manual.Days)
                {
                    mesocycle.Sessions.Add(BuildManualSession(plan, mesocycle.Id, week, day, catalog, now));
                }
            nextIndex = NumberSessions(mesocycle, nextIndex);
            plan.Mesocycles.Add(mesocycle);
        }

        db.TrainingPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.TrainingPlans.AsNoTracking().AnyAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct)) throw;
            return PlanResult<PlanView>.Fail(PlanFailure.ActivePlanExists, "Ya tienes un plan activo. Archívalo antes de crear otro.");
        }
        return PlanResult<PlanView>.Ok(ToView(plan, calendar ? PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today) : new PlanPosition(PlanTimeline.InProgress, 1)));
    }

    private static PlannedSession BuildManualSession(
        TrainingPlan plan, Guid mesocycleId, int week, ManualDay day, IReadOnlyDictionary<string, TrainingExerciseCandidate> catalog, DateTimeOffset now)
    {
        var prescription = day.Exercises.Select((exercise, index) =>
        {
            var candidate = catalog[exercise.ExerciseId];
            return new TrainingExercisePrescription(
                candidate.ExerciseId, candidate.CatalogVersion, candidate.Name, index + 1, exercise.Sets,
                exercise.MinRepetitions, exercise.MaxRepetitions, exercise.TargetRir, exercise.RestSeconds,
                TrainingProgressionRule.DoubleProgression, "Elegido por el usuario.",
                candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups);
        }).ToList();
        var minutes = 5 + (int)Math.Ceiling(day.Exercises.Sum(e => e.Sets * (e.RestSeconds + 45)) / 60.0);
        return new PlannedSession
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = mesocycleId, WeekNumber = week,
            // A sequence plan has no calendar: its sessions carry no date and follow the order of the routine.
            Date = IsSequence(plan) ? null : PlanSchedule.DateFor(plan.StartDate, week, day.Day!.Value), Title = day.Title,
            EstimatedDurationMinutes = Math.Clamp(minutes, 10, 240),
            PrescriptionJson = PrescriptionJson.Serialize(prescription,
                day.Exercises.Where(e => e.SupersetGroup is not null).ToDictionary(e => e.ExerciseId, e => e.SupersetGroup!)),
            Status = PlannedSessionStatus.Planned, UpdatedAt = now
        };
    }

    /// <summary>
    /// Rewrites the routines of the user's own plan from today on. Sessions already logged and past days stay as they are;
    /// every pending session from today onwards is replaced by the new routines (loads set by the adaptation are lost, the
    /// user's edit wins). The plan's length and start date do not change.
    /// </summary>
    public async Task<PlanResult<PlanView>> UpdateManualAsync(Guid userId, ManualPlan manual, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<PlanView>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        if (plan.Origin != TrainingPlanOrigin.Manual)
            return PlanResult<PlanView>.Fail(PlanFailure.NotAllowed, "Solo se pueden editar los planes que has creado tú.");

        var catalog = (await LoadCandidatesAsync(ct)).Candidates.ToDictionary(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        for (var d = 0; d < manual.Days.Count; d++)
            for (var e = 0; e < manual.Days[d].Exercises.Count; e++)
                if (!catalog.ContainsKey(manual.Days[d].Exercises[e].ExerciseId))
                    errors[$"days.{d + 1}.exercises.{e + 1}"] = ["El ejercicio no existe en el catálogo."];
        if (errors.Count > 0)
            return PlanResult<PlanView>.Fail(PlanFailure.InvalidRequest, "Algún ejercicio no existe en el catálogo.", errors);

        // The plan keeps its way of scheduling: a calendar plan needs a weekday on every day, a sequence plan ignores them.
        var sequence = IsSequence(plan);
        if (!sequence && manual.Days.Any(d => d.Day is null))
            return PlanResult<PlanView>.Fail(PlanFailure.InvalidRequest, "Cada día de un plan con calendario necesita su día de la semana.",
                new Dictionary<string, string[]> { ["days"] = ["Indica el día de la semana de cada rutina."] });

        var now = clock.GetUtcNow();
        var logged = (await db.WorkoutLogs.Where(l => l.PlanId == plan.Id).Select(l => l.PlannedSessionId).ToListAsync(ct)).ToHashSet();
        var sessions = await db.PlannedSessions.Where(s => s.PlanId == plan.Id).ToListAsync(ct);
        // Sequence plans: the routine position (0-based, in program order within its period) of every logged session, so the
        // new routines fill only the positions that are still free in that period.
        var positionOf = sequence
            ? sessions.GroupBy(s => s.WeekNumber).SelectMany(g => g.OrderBy(s => s.SequenceIndex).Select((s, i) => (s, i)))
                .Where(p => logged.Contains(p.s.Id)).ToDictionary(p => p.s.Id, p => p.i)
            : [];
        var loggedPositions = sessions.Where(s => positionOf.ContainsKey(s.Id)).Select(s => (s.WeekNumber, positionOf[s.Id])).ToHashSet();
        var loggedDates = sessions.Where(s => logged.Contains(s.Id)).Select(s => s.Date).ToHashSet();
        // Calendar: pending sessions from today on are replaced. Sequence: there is no "today", every pending session is.
        db.PlannedSessions.RemoveRange(sessions.Where(s => !logged.Contains(s.Id) && (sequence || s.Date >= Today)));

        plan.MacrocycleName = manual.Name;
        plan.MacrocycleNotes = manual.Notes;
        plan.TrainingDays = sequence ? [] : manual.Days.Select(d => d.Day.ToString()).ToArray();
        if (sequence) plan.SessionsPerPeriod = manual.Days.Count;
        plan.UpdatedAt = now;
        foreach (var phase in plan.Phases)
        {
            phase.Name = manual.Name;
            phase.Objective = manual.Notes.Length > 0 ? manual.Notes : "Plan manual";
        }
        // Kept sessions (logged, or past and unlogged in a calendar plan) plus the new ones, keyed to be numbered in program order.
        var ordered = new List<(int Mesocycle, int Week, int Position, PlannedSession Session)>();
        foreach (var kept in sessions.Where(s => logged.Contains(s.Id) || (!sequence && s.Date < Today)))
            ordered.Add((plan.Mesocycles.First(m => m.Id == kept.MesocycleId).Sequence, kept.WeekNumber, positionOf.GetValueOrDefault(kept.Id), kept));
        foreach (var mesocycle in plan.Mesocycles)
            for (var week = mesocycle.StartWeek; week < mesocycle.StartWeek + mesocycle.Weeks; week++)
                for (var position = 0; position < manual.Days.Count; position++)
                {
                    var day = manual.Days[position];
                    if (sequence ? loggedPositions.Contains((week, position))
                                 : PlanSchedule.DateFor(plan.StartDate, week, day.Day!.Value) is var date && (date < Today || loggedDates.Contains(date)))
                        continue;
                    var created = BuildManualSession(plan, mesocycle.Id, week, day, catalog, now);
                    db.PlannedSessions.Add(created);
                    ordered.Add((mesocycle.Sequence, week, position, created));
                }
        // Program order: block, period, then the position in the routine (sequence) or the date (calendar).
        var index = 1;
        foreach (var item in ordered
                     .OrderBy(o => o.Mesocycle).ThenBy(o => o.Week).ThenBy(o => sequence ? o.Position : 0)
                     .ThenBy(o => o.Session.Date).ThenBy(o => o.Session.Title, StringComparer.Ordinal))
            item.Session.SequenceIndex = index++;
        await db.SaveChangesAsync(ct);
        return PlanResult<PlanView>.Ok(ToView(plan, await PositionAsync(plan, ct)));
    }

    // ---------------------------------------------------------------- read

    public async Task<PlanView?> GetCurrentAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        return plan is null ? null : ToView(plan, await PositionAsync(plan, ct));
    }

    public async Task<WeekView?> GetCurrentWeekAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var position = await PositionAsync(plan, ct);
        if (position.State == PlanTimeline.Finished)
        {
            plan.Status = TrainingPlanStatus.Completed;
            plan.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return new WeekView(plan.Id, position.Week, WeekStartOf(plan, position.Week), WeekEndOf(plan, position.Week),
                nameof(PlanTimeline.Finished), null, [], await IsLockedAsync(plan, ct));
        }
        return await BuildWeekAsync(plan, position, ct);
    }

    // ---------------------------------------------------------------- where the plan is, in either mode

    private static bool IsSequence(TrainingPlan plan) => plan.SchedulingMode == PlanSchedulingMode.Sequence;

    private static IPlanTimeline TimelineOf(TrainingPlan plan) => IsSequence(plan) ? new SequenceTimeline() : new CalendarTimeline();

    private static DateOnly? WeekStartOf(TrainingPlan plan, int week) => IsSequence(plan) ? null : PlanSchedule.WeekStart(plan.StartDate, week);

    private static DateOnly? WeekEndOf(TrainingPlan plan, int week) => WeekStartOf(plan, week)?.AddDays(6);

    private static IReadOnlyList<MesocycleSlot> SlotsOf(TrainingPlan plan) =>
        PlanSchedule.Slots(plan.Phases.Select(p => new PeriodSpan(p.SequenceNumber, p.PhaseType, p.StartPeriod, p.EndPeriod)));

    /// <summary>
    /// Where the plan is. A calendar plan follows the clock. A sequence plan follows its sessions: the current period is the one of
    /// the first pending session, and the plan is finished only when every period has been generated and resolved. No amount of
    /// elapsed time moves it.
    /// </summary>
    private async Task<PlanPosition> PositionAsync(TrainingPlan plan, CancellationToken ct)
    {
        if (!IsSequence(plan)) return PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        var firstPending = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.Status == PlannedSessionStatus.Planned).OrderBy(s => s.SequenceIndex)
            .Select(s => (int?)s.WeekNumber).FirstOrDefaultAsync(ct);
        if (firstPending is { } week) return new PlanPosition(PlanTimeline.InProgress, week);
        var generated = plan.Mesocycles.Count == 0 ? 0 : plan.Mesocycles.Max(m => m.StartWeek + m.Weeks - 1);
        return generated >= plan.TotalWeeks
            ? new PlanPosition(PlanTimeline.Finished, plan.TotalWeeks)
            : new PlanPosition(PlanTimeline.InProgress, generated + 1);
    }

    public async Task<BlockView?> GetCurrentBlockAsync(Guid userId, CancellationToken ct)
    {
        // The current week generates the block when it is first needed, so the block always exists once this returns.
        var current = await GetCurrentWeekAsync(userId, ct);
        if (current is null) return null;
        if (current.Mesocycle is not { } meso) return new BlockView(current.PlanId, null, [], current.Locked);
        var plan = (await ActivePlanAsync(userId, ct))!;
        var last = meso.StartWeek + meso.Weeks - 1;
        var sessions = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.WeekNumber >= meso.StartWeek && s.WeekNumber <= last)
            .OrderBy(s => s.SequenceIndex).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => ids.Contains(l.PlannedSessionId)).ToListAsync(ct);
        var weeks = Enumerable.Range(meso.StartWeek, meso.Weeks).Select(week =>
        {
            var start = WeekStartOf(plan, week);
            return new BlockWeekView(week, start, start?.AddDays(6), week == current.WeekNumber,
                sessions.Where(s => s.WeekNumber == week).Select(s => ToSessionView(s, logs.FirstOrDefault(l => l.PlannedSessionId == s.Id))).ToList());
        }).ToList();
        return new BlockView(plan.Id, meso, weeks, current.Locked);
    }

    private const int RecoveryLookbackDays = 6;


    /// <summary>
    /// Resolves the next actionable session through the plan's timeline abstraction, not by asking which date is today.
    /// Calendar plans may therefore return an overdue session; sequence plans simply return the next unresolved item in
    /// rotation. Recovery alternatives are suggestions over later pending sessions and never advance or reorder the plan.
    /// </summary>
    public async Task<NextSessionView?> GetNextAsync(Guid userId, CancellationToken ct)
    {
        // Reading the current week generates the block when it is first needed, so the next session always exists once this returns.
        var current = await GetCurrentWeekAsync(userId, ct);
        if (current is null) return null;
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null || current.Locked)
        {
            var mode = await db.TrainingPlans.AsNoTracking().Where(p => p.Id == current.PlanId).Select(p => p.SchedulingMode).FirstAsync(ct);
            return new NextSessionView(current.PlanId, mode.ToString(), current.Timeline, null, null, null, false, [], current.Locked);
        }
        var timeline = TimelineOf(plan);

        var pending = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.Status == PlannedSessionStatus.Planned).OrderBy(s => s.SequenceIndex).ToListAsync(ct);
        var next = timeline.Next(
            pending.Select(s => new TimelineSession(s.Id, s.SequenceIndex, s.WeekNumber, s.Date, s.Title)).ToList(), Today,
            RecoveryAdvisor.AlternativeCandidates + 1);
        var overdue = next.Overdue.Select(s => new OverdueSessionView(s.Id, s.SequenceIndex, TitleOf(s.Title), s.Date!.Value)).ToList();
        if (next.Next is null)
            return new NextSessionView(plan.Id, timeline.Mode, current.Timeline, null, null, null, false, overdue);

        var session = pending.First(s => s.Id == next.Next.Id);
        var premium = await subscriptions.HasPremiumAsync(userId, ct);
        var reentry = await ReentryAsync(plan, session, premium, ct);
        if (reentry is { Applied: true })
        {
            // The session may have just been made lighter: show and assess what is stored now.
            session = await db.PlannedSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id, ct);
            pending[pending.FindIndex(s => s.Id == session.Id)] = session;
        }
        var logs = await db.WorkoutLogs.AsNoTracking().Where(l => l.PlanId == plan.Id && l.PlannedSessionId == session.Id).ToListAsync(ct);
        var last = await LastPerformanceAsync(userId, [session], premium, ct);
        var inPeriod = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id && s.WeekNumber == session.WeekNumber).Select(s => s.SequenceIndex).ToListAsync(ct);
        var totalInPeriod = inPeriod.Count;
        var mesocycle = plan.Mesocycles.FirstOrDefault(m => m.Id == session.MesocycleId);
        var position = new NextPositionView(session.WeekNumber, inPeriod.Count(i => i <= session.SequenceIndex), totalInPeriod,
            mesocycle is null ? null : new MesocycleView(mesocycle.Sequence, mesocycle.PhaseType, mesocycle.IsDeload, mesocycle.StartWeek, mesocycle.Weeks,
                Math.Max(1, session.WeekNumber - mesocycle.StartWeek + 1)));

        AvailabilityView? availability = null;
        if (premium)
        {
            var candidates = next.Candidates.Select(c => pending.First(s => s.Id == c.Id)).Select(s => new RecoveryCandidate(
                s.Id, s.SequenceIndex, s.Title,
                PrescriptionJson.Read(s.PrescriptionJson).Select(e => new RecoveryPlannedExercise(
                    e.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), e.SecondaryMuscleGroups.Select(m => m.ToString()).ToList(), e.Sets)).ToList())).ToList();
            var recommendation = RecoveryAdvisor.Recommend(
                candidates, await RecoveryHistoryAsync(userId, clock.GetUtcNow(), ct), clock.GetUtcNow(), Enum.Parse<TrainingExperience>(plan.Experience), Lang);
            availability = new AvailabilityView(
                recommendation.Next.State.ToString(), recommendation.Next.AvailableAt, recommendation.Next.Reasons,
                recommendation.Alternatives.Select(a => new RecoveryAlternativeView(a.SessionId, a.SequenceIndex, TitleOf(a.Title))).ToList());
        }

        return new NextSessionView(plan.Id, timeline.Mode, current.Timeline, ToSessionView(session, logs.FirstOrDefault(), last), position,
            availability, AvailabilityLocked: !premium, overdue, Reentry: reentry,
            Reschedule: await OfferRescheduleAsync(plan, ct));
    }

    /// <summary>Days without training after which a sequence plan suggests easing back in. The plan itself never moves while the user is away.</summary>
    public const int ReentryAfterDays = 21;

    /// <summary>
    /// After <see cref="ReentryAfterDays"/> days without training the user is told to ease back in and, with Premium, the first session
    /// is made lighter on its own (one set less in every exercise and one repetition more in reserve), once per absence. The plan does
    /// not move meanwhile; the usual restoring rules give the sets back after clean sessions. Reading the next session does this,
    /// like reading the week generates its block.
    /// </summary>
    private async Task<ReentryView?> ReentryAsync(TrainingPlan plan, PlannedSession next, bool premium, CancellationToken ct)
    {
        var lastWorkout = await db.WorkoutLogs.AsNoTracking().Where(l => l.PlanId == plan.Id)
            .OrderByDescending(l => l.PerformedAt).Select(l => (DateTimeOffset?)l.PerformedAt).FirstOrDefaultAsync(ct);
        if (lastWorkout is not { } at) return null;
        var days = (int)Math.Floor((clock.GetUtcNow() - at).TotalDays);
        if (days < ReentryAfterDays) return null;
        var applied = premium && await EaseBackInAsync(plan, next, at, ct);
        return new ReentryView(days, T(applied
            ? $"Llevas {days} días sin entrenar. Hemos aligerado esta sesión (una serie menos y una repetición más en reserva) para que vuelvas con buen pie; tu plan sigue donde lo dejaste."
            : $"Llevas {days} días sin entrenar. Tu plan sigue donde lo dejaste: empieza con cargas algo más ligeras y una serie menos en la primera sesión."), applied);
    }

    public const string ReentryKind = "reentry";

    private async Task<RescheduleOfferView?> OfferRescheduleAsync(TrainingPlan plan, CancellationToken ct)
    {
        var behind = await WeeksBehindAsync(plan, await PositionAsync(plan, ct), ct);
        return behind >= 1 ? new RescheduleOfferView(behind) : null;
    }

    /// <summary>True when the session is (now or already) lighter for this absence; false when nothing could be lightened.</summary>
    private async Task<bool> EaseBackInAsync(TrainingPlan plan, PlannedSession next, DateTimeOffset lastWorkout, CancellationToken ct)
    {
        if (await db.PlanAdaptations.AsNoTracking().AnyAsync(a => a.PlanId == plan.Id && a.Kind == ReentryKind && a.EvidenceAt == lastWorkout, ct))
            return true;
        // A deload is already as light as it gets.
        if (plan.Mesocycles.FirstOrDefault(m => m.Id == next.MesocycleId) is { IsDeload: true } || PrescriptionJson.IsAdaptationDeload(next.PrescriptionJson))
            return false;
        var session = await db.PlannedSessions.FirstAsync(s => s.Id == next.Id, ct);
        var ids = PrescriptionJson.Read(session.PrescriptionJson).Select(e => e.ExerciseId).ToHashSet(StringComparer.Ordinal);
        var json = session.PrescriptionJson;
        var changed = false;
        if (TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json, ids, out var fewer, out _)) { json = fewer; changed = true; }
        if (TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var reserve, out _)) { json = reserve; changed = true; }
        if (!changed) return false;

        var now = clock.GetUtcNow();
        session.PrescriptionJson = json;
        session.UpdatedAt = now;
        db.PlanAdaptations.Add(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Kind = ReentryKind, ExerciseIds = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            SessionsChanged = 1, AppliedAt = now, EvidenceAt = lastWorkout,
            Explanation = "Primera sesión tras una ausencia larga: una serie menos en cada ejercicio y una repetición más en reserva."
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request lightened it first (the unique index lets only one): nothing is changed twice.
            db.ChangeTracker.Clear();
            return true;
        }
        return true;
    }

    // ---------------------------------------------------------------- reschedule from today (calendar plans)

    /// <summary>
    /// How many whole weeks the pending program is behind the calendar: the current week minus the first week that still has
    /// something to do. Zero or negative means it is on time (or ahead).
    /// </summary>
    private async Task<int> WeeksBehindAsync(TrainingPlan plan, PlanPosition position, CancellationToken ct)
    {
        if (IsSequence(plan) || position.State != PlanTimeline.InProgress) return 0;
        var firstPending = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.Status == PlannedSessionStatus.Planned).Select(s => (int?)s.WeekNumber).MinAsync(ct);
        // Everything generated is resolved: the next block starts right after the last one generated.
        var first = firstPending ?? (plan.Mesocycles.Count == 0 ? 1 : plan.Mesocycles.Max(m => m.StartWeek + m.Weeks - 1) + 1);
        return position.Week - first;
    }

    public async Task<PlanResult<RescheduleResult>> RescheduleAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<RescheduleResult>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        if (IsSequence(plan))
            return PlanResult<RescheduleResult>.Fail(PlanFailure.NotAllowed, "Un plan sin días fijos no tiene calendario que recolocar: sigue donde lo dejaste.");
        if (await IsLockedAsync(plan, ct)) return PlanResult<RescheduleResult>.Fail(PlanFailure.PremiumRequired, LockedMessage);

        var position = PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        var behind = await WeeksBehindAsync(plan, position, ct);
        if (behind < 1) return PlanResult<RescheduleResult>.Ok(new(0, 0, plan.StartDate, plan.StartDate.AddDays(plan.TotalWeeks * 7 - 1)));

        // Whole weeks, so every session keeps its weekday. The pending program moves; what was already done is never rewritten (it keeps its planned date and its real one), and the
        // days moved are remembered so a late workout of the days skipped can still be registered.
        var days = behind * 7;
        var first = position.Week - behind;
        var moving = await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.WeekNumber >= first && s.Status == PlannedSessionStatus.Planned).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var session in moving) { session.Date = session.Date?.AddDays(days); session.UpdatedAt = now; }
        foreach (var phase in plan.Phases) { phase.StartDate = phase.StartDate?.AddDays(days); phase.EndDate = phase.EndDate?.AddDays(days); }
        plan.StartDate = plan.StartDate.AddDays(days);
        plan.ScheduleShiftDays += days;
        plan.UpdatedAt = now;
        db.PlanAdaptations.Add(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Kind = "reschedule", ExerciseIds = [], SessionsChanged = moving.Count, AppliedAt = now, EvidenceAt = now,
            Explanation = $"El programa pendiente se movió {behind} semana(s) para continuar desde esta semana."
        });
        await db.SaveChangesAsync(ct);
        return PlanResult<RescheduleResult>.Ok(new(behind, moving.Count, plan.StartDate, plan.StartDate.AddDays(plan.TotalWeeks * 7 - 1)));
    }

    /// <summary>The user's recent workouts (any plan) as the recovery advisor needs them: sets done per exercise and how it felt.</summary>
    private async Task<IReadOnlyList<RecoveryExposure>> RecoveryHistoryAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-RecoveryLookbackDays);
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets)
            .Where(l => l.UserId == userId && l.PerformedAt >= since).OrderBy(l => l.PerformedAt).ToListAsync(ct);
        var ids = logs.Select(l => l.PlannedSessionId).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var exposures = new List<RecoveryExposure>();
        foreach (var log in logs)
        {
            if (!sessions.TryGetValue(log.PlannedSessionId, out var session)) continue;
            var loaded = LoadedExercises(log.ExerciseFeedbackJson);
            var prescribed = PrescriptionJson.Read(session.PrescriptionJson);
            var exercises = new List<RecoveryExercise>();
            foreach (var group in log.Sets.Where(s => s.Completed).GroupBy(s => s.ExerciseId, StringComparer.OrdinalIgnoreCase))
            {
                var exercise = prescribed.FirstOrDefault(e => string.Equals(e.ExerciseId, group.Key, StringComparison.OrdinalIgnoreCase));
                if (exercise is null) continue;
                var rirs = group.Where(s => s.Rir is not null).Select(s => s.Rir!.Value).ToList();
                exercises.Add(new RecoveryExercise(
                    exercise.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), exercise.SecondaryMuscleGroups.Select(m => m.ToString()).ToList(),
                    group.Count(), rirs.Count == group.Count() ? rirs.Average() : null, loaded.Contains(group.Key)));
            }
            exposures.Add(new RecoveryExposure(log.PerformedAt, exercises, log.Recovery, log.SessionRpe, log.PainReported));
        }
        return exposures;
    }

    private static HashSet<string> LoadedExercises(string? json)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            foreach (var item in System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateArray())
                if (item.TryGetProperty("exerciseId", out var id) && item.TryGetProperty("preFatigue", out var fatigue)
                    && string.Equals(fatigue.GetString(), "loaded", StringComparison.OrdinalIgnoreCase) && id.GetString() is { } name)
                    result.Add(name);
        }
        catch (System.Text.Json.JsonException) { }
        return result;
    }

    public async Task<IReadOnlyList<HistoryItem>?> GetHistoryAsync(Guid userId, int take, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.PlanId == plan.Id)
            .OrderByDescending(l => l.PerformedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var ids = logs.Select(l => l.PlannedSessionId).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return logs.Select(log =>
        {
            var session = sessions[log.PlannedSessionId];
            var planned = PrescriptionJson.Read(session.PrescriptionJson).Sum(e => e.Sets);
            return new HistoryItem(session.Id, session.WeekNumber, session.Date, TitleOf(session.Title), log.Outcome.ToString(),
                log.LoggedAt, log.Sets.Count(s => s.Completed), planned, log.PerformedAt);
        }).ToList();
    }

    /// <summary>
    /// What the user has lifted over time, across every plan they ever had: per exercise (load, estimated one-repetition
    /// maximum, records), weekly sets per muscle and the streak. Read-only, so it also works for a lapsed Premium.
    /// </summary>
    public async Task<ProgressView> GetProgressAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var zone = await zones.ResolveAsync(userId, ct);
        var rows = await (from set in db.WorkoutSetLogs.AsNoTracking()
                          join log in db.WorkoutLogs.AsNoTracking() on set.WorkoutLogId equals log.Id
                          where log.UserId == userId && set.Completed
                          select new { set.ExerciseId, LogId = log.Id, LoggedAt = log.PerformedAt, set.WeightKg, set.Repetitions }).ToListAsync(ct);
        var ids = rows.Select(r => r.ExerciseId).Distinct().ToList();
        var catalog = await db.ExerciseCatalog.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.NameEs, e.PrimaryMuscles, e.SecondaryMuscles }).ToListAsync(ct);
        var info = catalog.ToDictionary(e => e.Id, e => new ProgressExerciseInfo(e.NameEs, e.PrimaryMuscles, e.SecondaryMuscles));
        var goal = (await ActivePlanAsync(userId, ct))?.SessionsPerPeriod ?? 0;
        return ProgressCalculator.Build(
            rows.Select(r => new ProgressSetRow(r.ExerciseId, r.LogId, r.LoggedAt, r.WeightKg, r.Repetitions)).ToList(), info, zone, Today, goal);
    }

    public async Task<PlanLoadView?> GetLoadAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var position = await PositionAsync(plan, ct);
        var started = position.State != PlanTimeline.NotStarted;

        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id)
            .Select(s => new { s.Id, s.WeekNumber, s.PrescriptionJson }).ToListAsync(ct);
        var logs = await db.WorkoutLogs.AsNoTracking().Where(l => l.PlanId == plan.Id)
            .Select(l => new { l.PlannedSessionId, Completed = l.Sets.Count(s => s.Completed) }).ToListAsync(ct);
        var weekOfSession = sessions.ToDictionary(s => s.Id, s => s.WeekNumber);
        var plannedByWeek = sessions.GroupBy(s => s.WeekNumber)
            .ToDictionary(g => g.Key, g => (Sets: g.Sum(s => PrescriptionJson.Read(s.PrescriptionJson).Sum(e => e.Sets)), Sessions: g.Count()));
        var loggedByWeek = logs.Where(l => weekOfSession.ContainsKey(l.PlannedSessionId))
            .GroupBy(l => weekOfSession[l.PlannedSessionId])
            .ToDictionary(g => g.Key, g => (Sets: g.Sum(l => l.Completed), Sessions: g.Count()));

        var weeks = new List<WeekLoadView>(plan.TotalWeeks);
        for (var week = 1; week <= plan.TotalWeeks; week++)
        {
            var start = WeekStartOf(plan, week);
            var phase = plan.Phases.FirstOrDefault(p => week >= p.StartPeriod && week <= p.EndPeriod)?.PhaseType;
            var hasPlanned = plannedByWeek.TryGetValue(week, out var planned);
            var reached = started && week <= position.Week;
            loggedByWeek.TryGetValue(week, out var logged);
            weeks.Add(new WeekLoadView(week, start, phase, phase == "deload",
                hasPlanned ? planned.Sets : null, reached ? logged.Sets : null,
                hasPlanned ? planned.Sessions : 0, logged.Sessions));
        }
        return new PlanLoadView(plan.Id, plan.TotalWeeks, position.Week, "sets", weeks, IsSequence(plan) ? "Rotation" : "Week",
            await RollingLoadAsync(plan, sessions.Select(s => (s.WeekNumber, s.PrescriptionJson)).ToList(), position, ct));
    }

    /// <summary>
    /// What the user really received in the last seven days (from the instants of their workouts, whatever the plan period) against what
    /// the current block prescribes per seven days. A period of a plan is a group of sessions, so it is never used as if it were time.
    /// </summary>
    private async Task<RollingLoadView> RollingLoadAsync(
        TrainingPlan plan, IReadOnlyList<(int Week, string Json)> sessions, PlanPosition position, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now.AddDays(-LoadMetrics.PaceWindowDays);
        var recent = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets)
            .Where(l => l.UserId == plan.UserId && l.PerformedAt > since && l.PerformedAt <= now).ToListAsync(ct);
        var prescribed = sessions.Where(s => s.Week >= 1).ToList();
        var ids = recent.SelectMany(l => l.Sets.Select(s => s.ExerciseId)).Concat(prescribed.SelectMany(s => PrescriptionJson.Read(s.Json).Select(e => e.ExerciseId)))
            .Distinct().ToList();
        var muscles = (await db.ExerciseCatalog.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.PrimaryMuscles, e.SecondaryMuscles }).ToListAsync(ct))
            .ToDictionary(e => e.Id, e => (Primary: (IReadOnlyList<string>)e.PrimaryMuscles, Secondary: (IReadOnlyList<string>)e.SecondaryMuscles));

        var windowStart = now.AddDays(-7);
        var workouts = recent.Where(l => l.PerformedAt > windowStart).Select(l => new DoseWorkout(l.PerformedAt,
            l.Sets.Where(s => s.Completed && muscles.ContainsKey(s.ExerciseId)).GroupBy(s => s.ExerciseId)
                .Select(g => new DoseExercise(muscles[g.Key].Primary, muscles[g.Key].Secondary, g.Count())).ToList())).ToList();

        var slot = SlotsOf(plan).FirstOrDefault(s => s.Contains(position.Week));
        var blockSessions = slot is null ? [] : prescribed.Where(s => slot.Contains(s.Week)).ToList();
        var perSession = blockSessions.Select(s => (IReadOnlyList<DoseExercise>)PrescriptionJson.Read(s.Json)
            .Select(e => new DoseExercise(e.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), e.SecondaryMuscleGroups.Select(m => m.ToString()).ToList(), e.Sets)).ToList());
        return new RollingLoadView(7, windowStart, now, workouts.Count, plan.SessionsPerPeriod,
            LoadMetrics.ObservedSessionsPerSevenDays(recent.Select(l => l.PerformedAt).ToList(), now),
            LoadMetrics.Dose(workouts), slot is null ? [] : LoadMetrics.PrescribedPerSevenDays(perSession, slot.Weeks));
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

    /// <summary>
    /// Stores a result against its planned session, even when the workout happened later (or is registered after a newer one).
    /// Idempotency is checked before the active-session state is changed; adaptation is then gated by workout chronology so
    /// an old result enriches history without rolling the already-adapted future prescription backwards.
    /// </summary>
    public async Task<PlanResult<LogResult>> LogSessionAsync(Guid userId, Guid sessionId, ParsedLog log, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<LogResult>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        var session = await db.PlannedSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlanId == plan.Id, ct);
        if (session is null) return PlanResult<LogResult>.Fail(PlanFailure.SessionNotFound, "La sesión no existe.");
        // A retry of a request that already succeeded (the client lost the answer) must not fail: when the stored log is
        // the same as the one sent, the saved result is returned again. A different log for the session stays a conflict.
        var existing = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).FirstOrDefaultAsync(l => l.PlannedSessionId == session.Id, ct);
        if (log.ClientRequestId is { } requestKey)
        {
            var byKey = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets)
                .FirstOrDefaultAsync(l => l.UserId == userId && l.ClientRequestId == requestKey, ct);
            if (byKey is not null && byKey.PlannedSessionId != session.Id)
                return PlanResult<LogResult>.Fail(PlanFailure.InvalidRequest, "Ese identificador de petición ya se usó para otra sesión.",
                    new Dictionary<string, string[]> { ["clientRequestId"] = ["Ya se usó para otra sesión."] });
            existing ??= byKey;
        }
        if (existing is not null)
            return await ReplayOrConflictAsync(session, existing, log, ct);
        if (await IsLockedAsync(plan, ct)) return PlanResult<LogResult>.Fail(PlanFailure.PremiumRequired, LockedMessage);
        // There is no window: a session can be registered whenever the plan is active, however late, because the program
        // order (SequenceIndex) and the real time of the workout (PerformedAt) are what the engine reads, not the planned date.
        if (session.Status != PlannedSessionStatus.Planned)
            return PlanResult<LogResult>.Fail(PlanFailure.AlreadyLogged, "Esta sesión ya está registrada.");

        var now = clock.GetUtcNow();
        var zone = await zones.ResolveAsync(userId, ct);
        var performedAt = log.PerformedAt ?? now;
        if (performedAt > now.AddMinutes(FutureToleranceMinutes))
            return PlanResult<LogResult>.Fail(PlanFailure.InvalidRequest, "La fecha del entrenamiento no puede estar en el futuro.",
                new Dictionary<string, string[]> { ["performedAt"] = ["No puede estar en el futuro."] });
        if (performedAt > now) performedAt = now;
        var performedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(performedAt, zone).DateTime);
        if (performedOn < plan.StartDate.AddDays(-plan.ScheduleShiftDays))
            return PlanResult<LogResult>.Fail(PlanFailure.InvalidRequest, "El entrenamiento no puede ser anterior al inicio del plan.",
                new Dictionary<string, string[]> { ["performedAt"] = ["Es anterior al inicio del plan."] });

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

        var workout = new WorkoutLog
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, PlannedSessionId = session.Id, LoggedAt = now,
            PerformedAt = performedAt, PerformedOn = performedOn, TimeZoneId = zone.Id, ClientRequestId = log.ClientRequestId,
            Outcome = log.Outcome, NonCompletionReason = log.NonCompletionReason, Recovery = log.Recovery,
            SessionRpe = log.SessionRpe, PainReported = log.Pain, Notes = log.Notes,
            ExerciseFeedbackJson = log.ExerciseFeedback is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(log.ExerciseFeedback.Select(p => new { exerciseId = p.Key, p.Value.Stimulus, p.Value.Discomfort, p.Value.PreFatigue }), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })
                : null
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

        // Automatic adaptation is a Premium feature: without access the session is still recorded, but nothing is adapted.
        var report = await subscriptions.HasPremiumAsync(userId, ct)
            ? await AdaptAsync(plan, session, workout, prescribed, now, ct)
            : new AdaptationReport([], "none", AdaptationLockedMessage, [], [], Locked: true);
        report = Localized(report);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrent = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).FirstOrDefaultAsync(l => l.PlannedSessionId == session.Id, ct);
            if (concurrent is null) throw;
            // Two identical requests at once: the second one gets the first one's result.
            var stored = await db.PlannedSessions.AsNoTracking().FirstAsync(s => s.Id == session.Id, ct);
            return await ReplayOrConflictAsync(stored, concurrent, log, ct);
        }
        return PlanResult<LogResult>.Ok(new LogResult(ToSessionView(session, workout), report));
    }

    private async Task<PlanResult<LogResult>> ReplayOrConflictAsync(PlannedSession session, WorkoutLog existing, ParsedLog sent, CancellationToken ct)
    {
        var sameRequest = sent.ClientRequestId is { } key && existing.ClientRequestId == key;
        if (!sameRequest && !SameLog(existing, sent))
            return PlanResult<LogResult>.Fail(PlanFailure.AlreadyLogged, "Esta sesión ya está registrada.");
        var applied = (await db.PlanAdaptations.AsNoTracking().Where(a => a.WorkoutLogId == existing.Id).OrderBy(a => a.AppliedAt).ToListAsync(ct))
            .Select(a => new AppliedAdaptationView(a.Kind, a.ExerciseIds, a.SessionsChanged, a.Explanation)).ToList();
        var report = Localized(new AdaptationReport([], "none", "Este registro ya estaba guardado.", applied, []));
        return PlanResult<LogResult>.Ok(new LogResult(ToSessionView(session, existing), report, Replayed: true));
    }

    /// <summary>True when the stored log holds exactly what the request describes (outcome, notes and every set).</summary>
    private static bool SameLog(WorkoutLog stored, ParsedLog sent)
    {
        if (stored.Outcome != sent.Outcome || stored.NonCompletionReason != sent.NonCompletionReason || stored.Recovery != sent.Recovery
            || stored.SessionRpe != sent.SessionRpe || stored.PainReported != sent.Pain || (stored.Notes ?? "") != (sent.Notes ?? "")
            || stored.Sets.Count != sent.Sets.Count)
            return false;
        return sent.Sets.All(set => stored.Sets.Any(s =>
            string.Equals(s.ExerciseId, set.ExerciseId, StringComparison.OrdinalIgnoreCase) && s.SetNumber == set.SetNumber
            && s.WeightKg == set.WeightKg && s.Repetitions == set.Repetitions && s.Rir == set.Rir && s.Completed == set.Completed
            && s.SkippedReason == set.SkippedReason && s.PainReported == set.Pain));
    }

    /// <summary>
    /// Reads the plan history, rebuilds the engine's evidence in performed-at order, evaluates session-level and exercise-level
    /// feedback, then applies accepted actions only to unresolved sessions beyond the furthest resolved program index. The
    /// evidence timestamp (not request time) anchors cooldowns; this makes late registration safe and prevents it from
    /// consuming future work.
    /// </summary>
    private async Task<AdaptationReport> AdaptAsync(
        TrainingPlan plan, PlannedSession logged, WorkoutLog workout, IReadOnlyList<TrainingExercisePrescription> prescribed,
        DateTimeOffset now, CancellationToken ct)
    {
        var mesocycles = await db.TrainingMesocycles.AsNoTracking().Where(m => m.PlanId == plan.Id).ToDictionaryAsync(m => m.Id, ct);
        var pastLogs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets)
            .Where(l => l.PlanId == plan.Id && l.Id != workout.Id).OrderByDescending(l => l.PerformedAt).ThenByDescending(l => l.LoggedAt)
            .Take(RecentLogsForHistory).ToListAsync(ct);
        // The chronology of the evidence is when the workouts HAPPENED, not when they were registered.
        var logs = pastLogs.Append(workout).OrderBy(l => l.PerformedAt).ThenBy(l => l.LoggedAt).ToList();
        var evidenceNow = logs[^1].PerformedAt;
        // A late log (older than a workout already saved) enters the history but must not change the plan: the plan was
        // already adapted with newer data, and applying an older reading on top would contradict it.
        var isHead = workout.PerformedAt >= evidenceNow;
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
                    log.PerformedAt, exercise.ExerciseId, exercise.Sets, exercise.MinRepetitions, exercise.MaxRepetitions,
                    exercise.TargetRepetitionsInReserve, exercise.ProgressionRule, eligible, sets, log.Outcome, log.Recovery,
                    original.TryGetValue(exercise.ExerciseId, out var dose) ? dose : null));
            }
            var plannedSets = exercises.Sum(e => e.Sets);
            sessionRecords.Add(new SessionRecord(log.PerformedAt, log.Outcome, log.NonCompletionReason, log.Recovery, log.PainReported,
                log.SessionRpe, plannedSets > 0 ? Math.Min(1m, (decimal)log.Sets.Count(s => s.Completed) / plannedSets) : null));
        }

        // Cooldowns run on the timeline of the evidence (EvidenceAt), so a late registration cannot start or skip one.
        var recent = (await db.PlanAdaptations.AsNoTracking().Where(a => a.PlanId == plan.Id).ToListAsync(ct))
            .Where(a => (a.EvidenceAt ?? a.AppliedAt) > evidenceNow.AddDays(-30))
            .Where(a => !a.Kind.StartsWith(MuscleKindPrefix, StringComparison.Ordinal))
            .Select(a => new TrainingAutoAdaptationRecord(KindOf(a.Kind), a.EvidenceAt ?? a.AppliedAt)).ToList();

        var outcome = AdaptationPlanner.Evaluate(new AdaptationInput(
            exposures, sessionRecords, prescribed.Select(e => e.ExerciseId).ToArray(), recent, evidenceNow));

        // Sessions that can still change: pending ones AFTER the furthest point of the program already resolved. This is the
        // program order (SequenceIndex), not the planned date: forgotten sessions of the past never use up an adaptation, and
        // a session logged out of order cannot touch the ones before it.
        var resolvedUpTo = Math.Max(logged.SequenceIndex, await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.Status != PlannedSessionStatus.Planned)
            .Select(s => (int?)s.SequenceIndex).MaxAsync(ct) ?? 0);
        var upcoming = isHead
            ? await db.PlannedSessions
                .Where(s => s.PlanId == plan.Id && s.Status == PlannedSessionStatus.Planned && s.SequenceIndex > resolvedUpTo)
                .OrderBy(s => s.SequenceIndex).Take(40).ToListAsync(ct)
            : [];
        var applied = new List<AppliedAdaptationView>();
        var lateNotes = new List<string>();
        if (!isHead)
            lateNotes.Add("Este entrenamiento es anterior a otros ya registrados: se guarda en el historial sin cambiar el plan.");

        foreach (var action in isHead ? outcome.Actions : [])
        {
            var targets = action.ExerciseIds.ToHashSet(StringComparer.Ordinal);
            var changed = 0;
            IEnumerable<PlannedSession> window = upcoming;
            if (action.Kind == TrainingAutoAdaptationKind.Deload)
                window = IsSequence(plan)
                    ? upcoming.Take(Math.Max(1, plan.SessionsPerPeriod)) // no calendar: the next rotation of sessions
                    : upcoming.Where(s => s.Date <= Today.AddDays(TrainingAutoAdaptationPolicy.DeloadWindowDays));
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
                SessionsChanged = changed, Explanation = action.Explanation, AppliedAt = now, EvidenceAt = evidenceNow
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

        var feedback = isHead ? await MuscleFeedbackAsync(plan, logged, workout, upcoming, now, evidenceNow, ct) : ([], []);
        applied.AddRange(feedback.Applied);

        return Localized(new AdaptationReport(
            outcome.Exercises.Select(a => new ExerciseAdviceView(a.ExerciseId, a.Action, a.Explanation, a.NextLoadKg, a.SuggestedRepetitions)).ToList(),
            outcome.SessionAction, outcome.SessionExplanation, applied, outcome.Notes.Concat(feedback.Notes).Concat(lateNotes).ToList()));

        static bool Set(out string target, string value) { target = value; return true; }
    }

    private const string MuscleKindPrefix = "muscle_";
    private const decimal MuscleWeeklyCeiling = 20m;
    private const int MuscleCooldownDays = 14;

    /// <summary>
    /// Reads the post-exercise answers (stimulus, discomfort, pre-fatigue) with the engine's muscle-feedback adapter: one set more
    /// after two sessions with low stimulus and good recovery, one set less after repeated high stimulus with fatigue. Discomfort is
    /// never automated: it only produces a note. Every change has a 14-day cooldown per exercise and is stored in the audit.
    /// </summary>
    private async Task<(List<AppliedAdaptationView> Applied, List<string> Notes)> MuscleFeedbackAsync(
        TrainingPlan plan, PlannedSession logged, WorkoutLog workout, List<PlannedSession> upcoming, DateTimeOffset now,
        DateTimeOffset evidenceNow, CancellationToken ct)
    {
        var applied = new List<AppliedAdaptationView>();
        var notes = new List<string>();
        var past = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.PlanId == plan.Id && l.Id != workout.Id && l.ExerciseFeedbackJson != null)
            .OrderByDescending(l => l.PerformedAt).Take(12).ToListAsync(ct);
        var logs = new[] { workout }.Concat(past).ToList();
        if (logs.All(l => string.IsNullOrWhiteSpace(l.ExerciseFeedbackJson))) return (applied, notes);

        var ids = logs.Select(l => l.PlannedSessionId).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        sessions[logged.Id] = logged;
        // The engine treats records with the same date as ONE session and orders them by date. Two workouts can happen on the same
        // day, so each log gets its own ordinal "day" in chronological order (what matters to the engine is the order and that they differ).
        var ordered = logs.Where(l => sessions.ContainsKey(l.PlannedSessionId)).OrderBy(l => l.PerformedAt).ThenBy(l => l.LoggedAt).ToList();
        var records = ordered.Select((l, index) => new TrainingMuscleFeedbackRecord(
            DateOnly.FromDayNumber(index), l.Outcome.ToString(), l.ExerciseFeedbackJson, sessions[l.PlannedSessionId].PrescriptionJson)).ToList();
        var week = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id && s.WeekNumber == logged.WeekNumber)
            .Select(s => s.PrescriptionJson).ToListAsync(ct);
        var planned = TrainingMuscleFeedbackMapper.CountPlannedEffectiveSets(week);
        var ceilings = planned.Keys.ToDictionary(m => m, _ => (int)MuscleWeeklyCeiling, StringComparer.Ordinal);
        var decisions = TrainingMuscleFeedbackAdapter.Evaluate(TrainingMuscleFeedbackMapper.Map(records), planned, ceilings);

        var cooling = (await db.PlanAdaptations.AsNoTracking()
            .Where(a => a.PlanId == plan.Id && a.Kind.StartsWith(MuscleKindPrefix)).ToListAsync(ct))
            .Where(a => (a.EvidenceAt ?? a.AppliedAt) > evidenceNow.AddDays(-MuscleCooldownDays))
            .SelectMany(a => a.ExerciseIds).ToHashSet(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (decision.Action is TrainingMuscleFeedbackAction.PauseExercise or TrainingMuscleFeedbackAction.SuggestSwap)
            {
                notes.Add(decision.Explanation);
                continue;
            }
            if (cooling.Contains(decision.ExerciseId)) continue;
            var delta = decision.Action == TrainingMuscleFeedbackAction.AddSet ? 1 : -1;
            var changed = 0;
            foreach (var future in upcoming.Take(MaximumFutureSessionsToAdapt))
                if (TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(future.PrescriptionJson, decision.ExerciseId, delta, out var updated, out _))
                {
                    future.PrescriptionJson = updated;
                    future.UpdatedAt = now;
                    changed++;
                }
            if (changed == 0) continue;
            var kind = MuscleKindPrefix + (delta > 0 ? "add_set" : "reduce_set");
            db.PlanAdaptations.Add(new PlanAdaptation
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, WorkoutLogId = workout.Id, Kind = kind, ExerciseIds = [decision.ExerciseId],
                SessionsChanged = changed, Explanation = decision.Explanation, AppliedAt = now, EvidenceAt = evidenceNow
            });
            applied.Add(new AppliedAdaptationView(kind, [decision.ExerciseId], changed, decision.Explanation));
            // One exercise can be proposed for several of its muscles at once: change it only once.
            cooling.Add(decision.ExerciseId);
        }
        return (applied, notes);
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
                a.ExerciseId, NameOf(a.ExerciseId, a.Name), a.MovementPattern, a.PrimaryMuscleGroups, a.RequiredEquipment, a.MatchLevel)).ToList(),
            Tn(alternatives.Message)));
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
                && s.Status == PlannedSessionStatus.Planned && s.SequenceIndex > context.Session.SequenceIndex).ToListAsync(ct));
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

    /// <summary>
    /// Adds one exercise to a single pending session. It is part of that session only: it is logged, counts for the history, the
    /// progress and the load like any other, but it is never copied to other sessions or blocks.
    /// </summary>
    public async Task<PlanResult<SessionView>> AddExerciseAsync(Guid userId, Guid sessionId, AddExerciseRequest request, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<SessionView>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        if (await IsLockedAsync(plan, ct)) return PlanResult<SessionView>.Fail(PlanFailure.PremiumRequired, LockedMessage);
        var session = await db.PlannedSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlanId == plan.Id, ct);
        if (session is null) return PlanResult<SessionView>.Fail(PlanFailure.SessionNotFound, "La sesión no existe.");
        var window = WindowCheck(plan, session);
        if (window is not null) return PlanResult<SessionView>.Fail(PlanFailure.SessionOutOfWindow, window);
        if (session.Status != PlannedSessionStatus.Planned)
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Solo se pueden añadir ejercicios a sesiones pendientes.");

        var errors = new Dictionary<string, string[]>();
        var sets = request.Sets ?? 3;
        var min = request.MinRepetitions ?? 8;
        var max = request.MaxRepetitions ?? Math.Max(min, 12);
        var rest = request.RestSeconds ?? 90;
        if (sets is < 1 or > 10) errors["sets"] = ["Las series deben estar entre 1 y 10."];
        if (min is < 1 or > 50) errors["minRepetitions"] = ["Las repeticiones mínimas deben estar entre 1 y 50."];
        if (max < min || max > 100) errors["maxRepetitions"] = ["Las repeticiones máximas deben ser al menos las mínimas y no pasar de 100."];
        if (request.TargetRir is < 0 or > 10) errors["targetRir"] = ["El RIR debe estar entre 0 y 10."];
        if (rest is < 0 or > 600) errors["restSeconds"] = ["El descanso debe estar entre 0 y 600 segundos."];
        if (errors.Count > 0)
            return PlanResult<SessionView>.Fail(PlanFailure.InvalidRequest, "Revisa los datos del ejercicio.", errors);

        var candidates = (await LoadCandidatesAsync(ct)).Candidates;
        var candidate = candidates.FirstOrDefault(c => string.Equals(c.ExerciseId, request.ExerciseId, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            return PlanResult<SessionView>.Fail(PlanFailure.InvalidRequest, "El ejercicio no existe en el catálogo.",
                new Dictionary<string, string[]> { ["exerciseId"] = ["El ejercicio no existe en el catálogo."] });
        var prescribed = PrescriptionJson.Read(session.PrescriptionJson);
        if (prescribed.Any(e => string.Equals(e.ExerciseId, candidate.ExerciseId, StringComparison.OrdinalIgnoreCase)))
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Ese ejercicio ya está en la sesión.");
        // The user chooses freely, but never something the profile rules out (injuries or exercises they excluded).
        var excluded = plan.ExcludedExerciseIds.Concat(LimitationRules.ExcludedExerciseIds(plan.Limitations, candidates))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excluded.Contains(candidate.ExerciseId))
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Ese ejercicio no es compatible con tu perfil.");

        var exercise = new TrainingExercisePrescription(
            candidate.ExerciseId, candidate.CatalogVersion, candidate.Name, prescribed.Count == 0 ? 1 : prescribed.Max(e => e.Order) + 1, sets,
            min, max, request.TargetRir ?? 2m, rest, TrainingProgressionRule.DoubleProgression, "Añadido por el usuario a esta sesión.",
            candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups);
        session.PrescriptionJson = PrescriptionJson.AppendExtra(session.PrescriptionJson, exercise);
        session.EstimatedDurationMinutes = Math.Clamp(session.EstimatedDurationMinutes + (int)Math.Ceiling(sets * (rest + 45) / 60.0), 10, 240);
        session.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return PlanResult<SessionView>.Ok(ToSessionView(session, null));
    }

    private sealed record SessionContext(
        PlanFailure? Failure, string? Message, TrainingPlan? Plan, PlannedSession? Session,
        TrainingExercisePrescription? Exercise, IReadOnlyList<TrainingExerciseCandidate>? Candidates, IReadOnlyList<TrainingExercisePrescription>? Prescribed);

    private async Task<SessionContext> SessionContextAsync(Guid userId, Guid sessionId, string exerciseId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return new(PlanFailure.NoActivePlan, "No tienes un plan activo.", null, null, null, null, null);
        if (await IsLockedAsync(plan, ct)) return new(PlanFailure.PremiumRequired, LockedMessage, null, null, null, null, null);
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

    private async Task<TrainingPlan?> ActivePlanAsync(Guid userId, CancellationToken ct)
    {
        var plan = await db.TrainingPlans.Include(p => p.Phases).Include(p => p.Mesocycles)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active, ct);
        _generatedPlan = plan is null || plan.Origin == TrainingPlanOrigin.Generated;
        return plan;
    }

    private async Task<WeekView> BuildWeekAsync(TrainingPlan plan, PlanPosition position, CancellationToken ct)
    {
        var slots = SlotsOf(plan);
        var slot = slots.First(s => s.Contains(position.Week));
        var locked = await IsLockedAsync(plan, ct);
        var mesocycle = plan.Mesocycles.FirstOrDefault(m => m.Sequence == slot.Sequence);
        if (mesocycle is null)
        {
            // A generated plan without Premium stays readable, but a GET must not silently create its next block.
            if (locked)
                return new WeekView(plan.Id, position.Week, WeekStartOf(plan, position.Week), WeekEndOf(plan, position.Week),
                    position.State.ToString(), null, [], Locked: true);
            mesocycle = await GenerateMesocycleAsync(plan, slot, ct);
            if (mesocycle is null)
                return new WeekView(plan.Id, position.Week, WeekStartOf(plan, position.Week), WeekEndOf(plan, position.Week),
                    "GenerationFailed", null, []);
        }

        var sessions = await db.PlannedSessions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id && s.WeekNumber == position.Week).OrderBy(s => s.SequenceIndex).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => ids.Contains(l.PlannedSessionId)).ToListAsync(ct);
        var weekStart = WeekStartOf(plan, position.Week);
        var last = await LastPerformanceAsync(plan.UserId, sessions, await subscriptions.HasPremiumAsync(plan.UserId, ct), ct);
        return new WeekView(
            plan.Id, position.Week, weekStart, weekStart?.AddDays(6), position.State.ToString(),
            new MesocycleView(mesocycle.Sequence, mesocycle.PhaseType, mesocycle.IsDeload, mesocycle.StartWeek, mesocycle.Weeks,
                position.Week - mesocycle.StartWeek + 1),
            sessions.Select(s => ToSessionView(s, logs.FirstOrDefault(l => l.PlannedSessionId == s.Id), last)).ToList(), locked);
    }

    /// <summary>
    /// The latest completed sets of every exercise in this week's sessions, from any session of the user
    /// (so a plan that starts after another one still shows what the user lifted before). The proposed load repeats the
    /// heaviest load of that day, or adds one increment when every completed set reached the top of the range.
    /// </summary>
    private async Task<Dictionary<string, LastPerformanceView>> LastPerformanceAsync(
        Guid userId, IReadOnlyList<PlannedSession> sessions, bool premium, CancellationToken ct)
    {
        var ranges = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)))
            ranges.TryAdd(e.ExerciseId, e.MaxRepetitions);
        var result = new Dictionary<string, LastPerformanceView>(StringComparer.Ordinal);
        if (ranges.Count == 0) return result;
        var ids = ranges.Keys.ToList();
        var rows = await (from set in db.WorkoutSetLogs.AsNoTracking()
                          join log in db.WorkoutLogs.AsNoTracking() on set.WorkoutLogId equals log.Id
                          where log.UserId == userId && ids.Contains(set.ExerciseId) && set.Completed && !set.PainReported
                          select new { set.ExerciseId, log.Id, LoggedAt = log.PerformedAt, log.PerformedOn, set.SetNumber, set.WeightKg, set.Repetitions, set.Rir })
            .ToListAsync(ct);
        foreach (var byExercise in rows.GroupBy(r => r.ExerciseId))
        {
            var latest = byExercise.GroupBy(r => r.Id).OrderByDescending(g => g.First().LoggedAt).First();
            var sets = latest.OrderBy(r => r.SetNumber).ToList();
            var heaviest = sets.Max(r => r.WeightKg);
            decimal? suggested = null;
            string? basis = null;
            if (heaviest is > 0)
            {
                // Proposing a heavier load is automatic progression, a Premium feature; everyone gets the last load to start from.
                var atTop = premium && sets.Where(r => r.WeightKg == heaviest).All(r => r.Repetitions >= ranges[byExercise.Key]);
                suggested = atTop ? heaviest + (heaviest < 20 ? 1m : 2.5m) : heaviest;
                basis = atTop ? "increase" : "repeat";
            }
            result[byExercise.Key] = new LastPerformanceView(
                sets[0].PerformedOn,
                sets.Select(r => new LastSetView(r.SetNumber, r.WeightKg, r.Repetitions, r.Rir)).ToList(), suggested, basis);
        }
        return result;
    }

    /// <summary>
    /// A generated plan is a Premium product: without access it can be read (and archived), but not used. Manual plans are
    /// free, and administrators always have access.
    /// </summary>
    private async Task<bool> IsLockedAsync(TrainingPlan plan, CancellationToken ct) =>
        plan.Origin == TrainingPlanOrigin.Generated && !await subscriptions.HasPremiumAsync(plan.UserId, ct);

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

        // The program order of the sessions that already exist says where in the rotation this block begins: a sequence plan
        // continues from its last session whatever the period or block boundary, and what is persisted is the only state it needs.
        var existingSessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id).Select(s => (int?)s.SequenceIndex).MaxAsync(ct) ?? 0;
        var firstIndex = existingSessions + 1;
        var profile = ProfileOf(plan);
        var generation = MesocycleGenerator.Generate(
            profile, plan.Seed, slot, candidates, previousIds,
            continuesPreviousBlock: previous is { IsDeload: false } && previous.Sequence == slot.Sequence - 1,
            loadReferences: await LoadReferencesAsync(plan.UserId, ct),
            rotationPosition: existingSessions);
        if (!generation.Succeeded) return null;

        var mesocycle = AddMesocycle(plan, slot, generation, clock.GetUtcNow(), firstIndex);
        // The plan is already tracked and the new block has a client-generated key: reached only through the plan's collection
        // EF would see it as an existing row (an UPDATE that affects nothing), so it is added explicitly.
        db.TrainingMesocycles.Add(mesocycle);
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

    /// <summary>Numbers the sessions of a block in program order (planned date, then title), continuing from <paramref name="firstIndex"/>.</summary>
    private static int NumberSessions(TrainingMesocycle mesocycle, int firstIndex)
    {
        // A sequence plan has no dates: its sessions keep the order they were generated in (a stable sort leaves them alone).
        foreach (var session in mesocycle.Sessions.OrderBy(s => s.WeekNumber).ThenBy(s => s.Date).ThenBy(s => s.Date is null ? string.Empty : s.Title, StringComparer.Ordinal))
            session.SequenceIndex = firstIndex++;
        return firstIndex;
    }

    private static TrainingMesocycle AddMesocycle(
        TrainingPlan plan, MesocycleSlot slot, MesocycleGeneration generation, DateTimeOffset now, int firstIndex)
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
        NumberSessions(mesocycle, firstIndex);
        plan.Mesocycles.Add(mesocycle);
        return mesocycle;
    }

    private static PlanProfile ProfileOf(TrainingPlan plan) => new(
        Enum.Parse<TrainingGoal>(plan.Goal), Enum.Parse<TrainingExperience>(plan.Experience), plan.StartDate, plan.TotalWeeks,
        IsSequence(plan) ? [] : plan.TrainingDays.Select(Enum.Parse<DayOfWeek>).ToList(),
        plan.MaxSessionMinutes, plan.AvailableEquipment,
        plan.ExcludedExerciseIds, plan.Limitations, Enum.Parse<TrainingProgramSplit>(plan.Split), plan.IncludeDeloadPhases,
        plan.DeloadEveryTrainingWeeks, plan.SchedulingMode, plan.SessionsPerPeriod);

    /// <summary>The most recent completed performance of each exercise, so a new block starts from real loads.</summary>
    private async Task<IReadOnlyList<TrainingLoadReference>> LoadReferencesAsync(Guid userId, CancellationToken ct)
    {
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.UserId == userId)
            .OrderByDescending(l => l.PerformedAt).Take(LoadReferenceLogs).ToListAsync(ct);
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
        if (IsSequence(plan)) return null; // no clock to measure against: any session of the plan can be used
        var position = PlanSchedule.Position(plan.StartDate, plan.TotalWeeks, Today);
        if (position.State == PlanTimeline.Finished) return "El plan ha terminado.";
        return session.WeekNumber == position.Week || session.WeekNumber == position.Week - 1
            ? null
            : "Solo puedes usar las sesiones de la semana en curso.";
    }

    // ---------------------------------------------------------------- views

    private PlanView ToView(TrainingPlan plan, PlanPosition position)
    {
        // The phase is read from the periods in both modes (the week of a calendar plan, the cycle of a sequence plan).
        var phase = position.State == PlanTimeline.Finished ? null
            : plan.Phases.OrderBy(p => p.SequenceNumber).FirstOrDefault(p => position.Week >= p.StartPeriod && position.Week <= p.EndPeriod);
        return new PlanView(
            plan.Id, plan.Status.ToString(), plan.Origin.ToString(), position.State.ToString(), position.Week, plan.TotalWeeks, plan.StartDate,
            IsSequence(plan) ? null : plan.StartDate.AddDays(plan.TotalWeeks * 7 - 1), plan.Goal, plan.Experience, plan.Split, plan.TrainingDays,
            plan.MaxSessionMinutes, plan.AvailableEquipment, plan.ExcludedExerciseIds, plan.Limitations, phase?.SequenceNumber,
            new MacrocycleView(
                plan.Origin == TrainingPlanOrigin.Generated ? T(plan.MacrocycleName) : plan.MacrocycleName,
                plan.Origin == TrainingPlanOrigin.Generated ? T(plan.MacrocycleNotes) : plan.MacrocycleNotes,
                plan.Phases.OrderBy(p => p.SequenceNumber)
                    .Select(p => new PhaseView(
                        p.SequenceNumber, plan.Origin == TrainingPlanOrigin.Generated ? T(p.Name) : p.Name, p.PhaseType, p.StartDate, p.EndDate,
                        T(p.Objective), p.StartPeriod, p.EndPeriod)).ToList()),
            T(plan.Origin == TrainingPlanOrigin.Manual ? ManualDisclaimer : Disclaimer),
            plan.SchedulingMode.ToString(), plan.SessionsPerPeriod);
    }

    private SessionView ToSessionView(PlannedSession session, WorkoutLog? log, IReadOnlyDictionary<string, LastPerformanceView>? last = null) => new(
        session.Id, session.WeekNumber, session.Date, session.Date?.DayOfWeek, TitleOf(session.Title), session.EstimatedDurationMinutes,
        session.Status.ToString(),
        PrescriptionJson.Read(session.PrescriptionJson).OrderBy(e => e.Order).Select(e => new ExerciseView(
            e.ExerciseId, NameOf(e.ExerciseId, e.ExerciseNameSnapshot), e.Order, e.Sets, e.MinRepetitions, e.MaxRepetitions, e.TargetRepetitionsInReserve,
            e.RestSeconds, e.ProgressionRule.ToString(), e.TargetLoadKg, Tn(e.LoadBasis), e.WarmupSets, Tn(e.WarmupNote),
            e.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), e.SecondaryMuscleGroups.Select(m => m.ToString()).ToList(),
            PrescriptionJson.SwappedFrom(session.PrescriptionJson, e.ExerciseId),
            last is not null && last.TryGetValue(e.ExerciseId, out var lp) ? lp : null,
            PrescriptionJson.SupersetGroup(session.PrescriptionJson, e.ExerciseId),
            PrescriptionJson.IsExtra(session.PrescriptionJson, e.ExerciseId))).ToList(),
        log is null ? null : new LogView(
            log.Id, log.LoggedAt, log.Outcome.ToString(), log.NonCompletionReason, log.Recovery, log.SessionRpe, log.PainReported, log.Notes,
            log.Sets.OrderBy(s => s.ExerciseId, StringComparer.Ordinal).ThenBy(s => s.SetNumber)
                .Select(s => new LoggedSetView(s.ExerciseId, s.SetNumber, s.WeightKg, s.Repetitions, s.Rir, s.Completed, s.SkippedReason, s.PainReported)).ToList(),
            log.PerformedAt, log.PerformedOn),
        session.SequenceIndex);
}
