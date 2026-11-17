namespace Trazo.Domain.Planning;

public enum TrainingPlanStatus
{
    Active,
    /// <summary>Every week of the plan has passed.</summary>
    Completed,
    /// <summary>Closed by the user to start another plan. Kept so the history is never lost.</summary>
    Archived
}

/// <summary>How a plan advances: by calendar weeks, or by the order of its sessions (the user trains on whatever days suit them).</summary>
public enum PlanSchedulingMode
{
    Calendar,
    Sequence
}

public enum TrainingPlanOrigin
{
    Manual,
    Generated
}

public enum PlannedSessionStatus
{
    Planned,
    Completed,
    /// <summary>Logged, but not everything prescribed was done.</summary>
    Partial,
    /// <summary>Logged as not done.</summary>
    Skipped
}

public enum WorkoutOutcome
{
    Completed,
    Partial,
    NotCompleted
}

/// <summary>
/// A user's training plan: the profile it was generated for and the macrocycle (a list of phases).
/// Only the phase summary is stored here; the detailed sessions are generated one mesocycle at a time
/// (see <see cref="TrainingMesocycle"/>), so future sessions do not exist until they are needed and can
/// use what the user actually did.
/// </summary>
public sealed class TrainingPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public TrainingPlanStatus Status { get; set; } = TrainingPlanStatus.Active;
    /// <summary>Immutable provenance: editing a generated plan never turns it into a manual plan.</summary>
    public TrainingPlanOrigin Origin { get; set; } = TrainingPlanOrigin.Generated;
    public required string Goal { get; set; }
    public required string Experience { get; set; }
    public required string Split { get; set; }
    public DateOnly StartDate { get; set; }
    /// <summary>Plan length in periods: weeks in a calendar plan, cycles (one pass through the session rotation) in a sequence plan.</summary>
    public int TotalWeeks { get; set; }
    public PlanSchedulingMode SchedulingMode { get; set; } = PlanSchedulingMode.Calendar;
    /// <summary>Sessions in one period: the training days of a calendar plan, the sessions of one rotation in a sequence plan.</summary>
    public int SessionsPerPeriod { get; set; }
    /// <summary>
    /// Days the calendar has been moved forward by rescheduling the plan from today. <see cref="StartDate"/> already includes them;
    /// the date a workout may be registered from is <c>StartDate - ScheduleShiftDays</c>, so rescheduling never closes the past.
    /// </summary>
    public int ScheduleShiftDays { get; set; }
    public string[] TrainingDays { get; set; } = [];
    public int MaxSessionMinutes { get; set; }
    /// <summary>Equipment tokens the user has. Empty means everything is available.</summary>
    public string[] AvailableEquipment { get; set; } = [];
    public string[] ExcludedExerciseIds { get; set; } = [];
    /// <summary>Body areas with a limitation or injury (see the options endpoint). Drives exercise exclusions.</summary>
    public string[] Limitations { get; set; } = [];
    public bool IncludeDeloadPhases { get; set; }
    /// <summary>Training weeks between deloads (4-8), or 0 when TRAZO places them automatically.</summary>
    public int DeloadEveryTrainingWeeks { get; set; } = 4;
    /// <summary>Makes selection reproducible: the same profile and seed always give the same plan.</summary>
    public int Seed { get; set; }
    public string EngineVersion { get; set; } = string.Empty;
    public string MacrocycleName { get; set; } = string.Empty;
    public string MacrocycleNotes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<TrainingPlanPhase> Phases { get; set; } = [];
    public List<TrainingMesocycle> Mesocycles { get; set; } = [];
}

/// <summary>One phase of the macrocycle (accumulation, intensification or deload). Summary only.</summary>
public sealed class TrainingPlanPhase
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public int SequenceNumber { get; set; }
    public required string Name { get; set; }
    public required string PhaseType { get; set; }
    /// <summary>First and last plan period (week, or cycle in a sequence plan) of the phase, 1-based. The dates exist only in calendar plans.</summary>
    public int StartPeriod { get; set; }
    public int EndPeriod { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public required string Objective { get; set; }
}

/// <summary>A generated block of consecutive weeks (at most four) inside one phase.</summary>
public sealed class TrainingMesocycle
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public int Sequence { get; set; }
    public int PhaseSequenceNumber { get; set; }
    public required string PhaseType { get; set; }
    public bool IsDeload { get; set; }
    /// <summary>First plan week (1-based) covered by this block.</summary>
    public int StartWeek { get; set; }
    public int Weeks { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<PlannedSession> Sessions { get; set; } = [];
}

public sealed class PlannedSession
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid MesocycleId { get; set; }
    /// <summary>Plan week, 1-based.</summary>
    public int WeekNumber { get; set; }
    /// <summary>Planned date. Null in a sequence plan: those sessions have no date, only a place in the program.</summary>
    public DateOnly? Date { get; set; }
    /// <summary>
    /// Position of the session in the program, 1-based and unique inside the plan. It is the identity of the session in the
    /// program order and never changes when the user logs the session on a different day than planned.
    /// </summary>
    public int SequenceIndex { get; set; }
    public required string Title { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    /// <summary>
    /// The prescription as JSON <c>{"exercises":[...]}</c>, the contract the engine's adaptation adjuster edits
    /// (sets, repetitions, RIR, rest, load target, progression rule, muscle groups).
    /// </summary>
    public required string PrescriptionJson { get; set; }
    public PlannedSessionStatus Status { get; set; } = PlannedSessionStatus.Planned;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>What the user did in one planned session, and how it felt.</summary>
public sealed class WorkoutLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PlannedSessionId { get; set; }
    /// <summary>When the log was REGISTERED in TRAZO (server clock). The time the workout happened is <see cref="PerformedAt"/>.</summary>
    public DateTimeOffset LoggedAt { get; set; }
    /// <summary>When the workout really happened. Defaults to the registration time; it is the chronology of all the evidence.</summary>
    public DateTimeOffset PerformedAt { get; set; }
    /// <summary>The local date of <see cref="PerformedAt"/> in <see cref="TimeZoneId"/> (the day the user trained).</summary>
    public DateOnly PerformedOn { get; set; }
    /// <summary>IANA zone used to compute <see cref="PerformedOn"/>.</summary>
    public string TimeZoneId { get; set; } = string.Empty;
    /// <summary>Optional idempotency key sent by the client: the same key always means the same request.</summary>
    public Guid? ClientRequestId { get; set; }
    public WorkoutOutcome Outcome { get; set; }
    /// <summary>None, Time, Fatigue, Equipment, Motivation, Illness or Other.</summary>
    public string NonCompletionReason { get; set; } = "None";
    /// <summary>Unknown, Good, Partial or Insufficient.</summary>
    public string Recovery { get; set; } = "Unknown";
    /// <summary>Whole-session effort, 0 to 10.</summary>
    public decimal? SessionRpe { get; set; }
    public bool PainReported { get; set; }
    public string? Notes { get; set; }
    /// <summary>
    /// Post-exercise answers, one object per exercise (the engine's format): <c>[{"exerciseId":"bench","stimulus":"adequate","discomfort":"none","preFatigue":"recovered"}]</c>.
    /// Collected for every account; only Premium adapts the plan from them.
    /// </summary>
    public string? ExerciseFeedbackJson { get; set; }

    public List<WorkoutSetLog> Sets { get; set; } = [];
}

public sealed class WorkoutSetLog
{
    public Guid Id { get; set; }
    public Guid WorkoutLogId { get; set; }
    public required string ExerciseId { get; set; }
    public int SetNumber { get; set; }
    public decimal? WeightKg { get; set; }
    public int? Repetitions { get; set; }
    public decimal? Rir { get; set; }
    public bool Completed { get; set; } = true;
    /// <summary>When a set was not done: fatigue, pain, time, equipment, illness, motivation or other.</summary>
    public string? SkippedReason { get; set; }
    public bool PainReported { get; set; }
}

/// <summary>Audit of the changes the engine made to future sessions on its own, also used for cooldowns.</summary>
public sealed class PlanAdaptation
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid? WorkoutLogId { get; set; }
    /// <summary>reduction_sets, raise_reserve, restore_sets, deload, hold_progression, load_update.</summary>
    public required string Kind { get; set; }
    public string[] ExerciseIds { get; set; } = [];
    public int SessionsChanged { get; set; }
    public required string Explanation { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    /// <summary>When the workout that caused the change happened (its <c>PerformedAt</c>). Cooldowns are measured from it. Null on old rows.</summary>
    public DateTimeOffset? EvidenceAt { get; set; }
}
