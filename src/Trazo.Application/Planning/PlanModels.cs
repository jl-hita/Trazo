namespace Trazo.Application.Planning;

// Views returned by the plan service. They only ever describe the macrocycle (phases) and the CURRENT week:
// no view contains the sessions of future weeks.

public sealed record PhaseView(int Sequence, string Name, string Type, DateOnly StartDate, DateOnly EndDate, string Objective);

public sealed record MacrocycleView(string Name, string Notes, IReadOnlyList<PhaseView> Phases);

public sealed record MesocycleView(int Sequence, string PhaseType, bool IsDeload, int StartWeek, int Weeks, int WeekInBlock);

public sealed record PlanView(
    Guid Id,
    string Status,
    string Origin,
    string Timeline,
    int CurrentWeek,
    int TotalWeeks,
    DateOnly StartDate,
    DateOnly EndDate,
    string Goal,
    string Experience,
    string Split,
    IReadOnlyList<string> TrainingDays,
    int MaxSessionMinutes,
    IReadOnlyList<string> AvailableEquipment,
    IReadOnlyList<string> ExcludedExerciseIds,
    IReadOnlyList<string> Limitations,
    int? CurrentPhase,
    MacrocycleView Macrocycle,
    string Disclaimer);

public sealed record ExerciseView(
    string ExerciseId,
    string Name,
    int Order,
    int Sets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRir,
    int RestSeconds,
    string ProgressionRule,
    decimal? TargetLoadKg,
    string? LoadBasis,
    int? WarmupSets,
    string? WarmupNote,
    IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> SecondaryMuscles,
    string? SwappedFromExerciseId,
    LastPerformanceView? LastPerformance = null,
    string? SupersetGroup = null);

/// <summary>One set of the last time the user did an exercise.</summary>
public sealed record LastSetView(int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir);

/// <summary>
/// What the user did the last time with an exercise (any plan) and a proposed working load for the next time:
/// <c>SuggestionBasis</c> is <c>repeat</c> (same load) or <c>increase</c> (every set reached the top of the range).
/// </summary>
public sealed record LastPerformanceView(DateOnly Date, IReadOnlyList<LastSetView> Sets, decimal? SuggestedLoadKg, string? SuggestionBasis);

public sealed record LoggedSetView(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool Pain);

public sealed record LogView(
    Guid Id, DateTimeOffset LoggedAt, string Outcome, string NonCompletionReason, string Recovery, decimal? SessionRpe,
    bool PainReported, string? Notes, IReadOnlyList<LoggedSetView> Sets, DateTimeOffset? PerformedAt = null, DateOnly? PerformedOn = null);

/// <summary><c>Date</c> is the planned date; <c>SequenceIndex</c> is the position of the session in the program (it never changes).</summary>
public sealed record SessionView(
    Guid Id, int WeekNumber, DateOnly Date, DayOfWeek Day, string Title, int EstimatedDurationMinutes, string Status,
    IReadOnlyList<ExerciseView> Exercises, LogView? Log, int SequenceIndex = 0);

public sealed record WeekView(
    Guid PlanId, int WeekNumber, DateOnly WeekStart, DateOnly WeekEnd, string Timeline, MesocycleView? Mesocycle,
    IReadOnlyList<SessionView> Sessions, bool Locked = false);

/// <summary>One week of the current block with its sessions (prescription and, once done, the log).</summary>
public sealed record BlockWeekView(int WeekNumber, DateOnly WeekStart, DateOnly WeekEnd, bool IsCurrent, IReadOnlyList<SessionView> Sessions);

/// <summary>
/// The whole current block (mesocycle, at most four weeks) so the user sees where the month is going: volume and intensity
/// per week. Later blocks are never served: they do not exist until their first week arrives.
/// </summary>
public sealed record BlockView(Guid PlanId, MesocycleView? Mesocycle, IReadOnlyList<BlockWeekView> Weeks, bool Locked = false);

public sealed record AppliedAdaptationView(string Kind, IReadOnlyList<string> ExerciseIds, int SessionsChanged, string Explanation);

public sealed record ExerciseAdviceView(string ExerciseId, string Action, string Explanation, decimal? NextLoadKg, IReadOnlyList<int>? SuggestedRepetitions);

public sealed record AdaptationReport(
    IReadOnlyList<ExerciseAdviceView> Exercises, string SessionAction, string SessionExplanation,
    IReadOnlyList<AppliedAdaptationView> Applied, IReadOnlyList<string> Notes, bool Locked = false);

/// <summary><c>Replayed</c> is true when the session had already been logged with exactly the same data: the saved result is returned again.</summary>
public sealed record LogResult(SessionView Session, AdaptationReport Adaptation, bool Replayed = false);

public sealed record AlternativeView(
    string ExerciseId, string Name, string MovementPattern, IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> RequiredEquipment, string MatchLevel);

public sealed record AlternativesResult(string ExerciseId, IReadOnlyList<AlternativeView> Alternatives, string? Message);

public sealed record HistoryItem(
    Guid SessionId, int WeekNumber, DateOnly Date, string Title, string Outcome, DateTimeOffset LoggedAt, int CompletedSets, int PlannedSets,
    DateTimeOffset? PerformedAt = null);

/// <summary>
/// Volume of one plan week, in sets. <see cref="PlannedSets"/> is null while the block of that week has not been generated
/// yet (blocks are generated when reached, so the future is never stored); <see cref="CompletedSets"/> is null for weeks
/// that have not started.
/// </summary>
public sealed record WeekLoadView(
    int Week, DateOnly WeekStart, string? PhaseType, bool IsDeload, int? PlannedSets, int? CompletedSets, int PlannedSessions, int LoggedSessions);

/// <summary>Weekly volume of the whole plan, one entry per plan week, to draw planned against real load.</summary>
public sealed record PlanLoadView(Guid PlanId, int TotalWeeks, int CurrentWeek, string Unit, IReadOnlyList<WeekLoadView> Weeks);

/// <summary>A pending session whose planned date has passed. It can still be registered, or skipped, whenever the user wants.</summary>
public sealed record OverdueSessionView(Guid Id, int SequenceIndex, string Title, DateOnly Date);

public sealed record RecoveryAlternativeView(Guid SessionId, int SequenceIndex, string Title);

/// <summary>
/// Whether the muscles of the session are recovered: <c>State</c> is Ready, Caution or Wait, <c>AvailableAt</c> the estimate (null when
/// ready), <c>Reasons</c> why it is better to wait, and <c>Alternatives</c> later pending sessions that are ready now. Advice only.
/// </summary>
public sealed record AvailabilityView(string State, DateTimeOffset? AvailableAt, IReadOnlyList<string> Reasons, IReadOnlyList<RecoveryAlternativeView> Alternatives);

/// <summary>Where the session is in the program: its period (week of the plan), place inside it and block.</summary>
public sealed record NextPositionView(int Period, int SessionInPeriod, int SessionsInPeriod, MesocycleView? Mesocycle);

/// <summary>
/// The session the user should do next, decided by the backend so no client has to work it out. <c>Session</c> is null when nothing
/// is pending (plan finished or locked). <c>Availability</c> is Premium: without it <c>AvailabilityLocked</c> is true.
/// </summary>
public sealed record NextSessionView(
    Guid PlanId, string SchedulingMode, string Timeline, SessionView? Session, NextPositionView? Position,
    AvailabilityView? Availability, bool AvailabilityLocked, IReadOnlyList<OverdueSessionView> Overdue, bool Locked = false);

public sealed record PlanOptions(
    IReadOnlyList<string> Goals, IReadOnlyList<string> Experience, IReadOnlyList<string> Splits,
    IReadOnlyList<string> Days, IReadOnlyList<string> Limitations, IReadOnlyList<string> Equipment,
    int MinimumTrainingDays, int MaximumTrainingDays, int MinimumSessionMinutes, int MaximumSessionMinutes,
    int MinimumTotalWeeks, int MaximumTotalWeeks, int DefaultTotalWeeks,
    IReadOnlyList<DayOption> DayOptions, string WeekStartsOn, string DefaultTimeZone);

/// <summary>A day of the week with a stable id (the English name used everywhere in the API), ISO number (Monday = 1) and Spanish labels.</summary>
public sealed record DayOption(string Id, int IsoNumber, string Label, string ShortLabel);

// Requests.

public sealed record LogSetRequest(
    string? ExerciseId, int? SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool? Completed,
    string? SkippedReason, bool? PainReported);

public sealed record LogSessionRequest(
    string? Outcome, string? NonCompletionReason, string? Recovery, decimal? SessionRpe, bool? PainReported,
    string? Notes, LogSetRequest[]? Sets, Dictionary<string, ExerciseFeedbackInput>? ExerciseFeedback = null,
    DateTimeOffset? PerformedAt = null, Guid? ClientRequestId = null);

/// <summary>The three post-exercise questions: stimulus low|adequate|high, discomfort none|mild|strong, pre-fatigue recovered|loaded.</summary>
public sealed record ExerciseFeedbackInput(string? Stimulus, string? Discomfort, string? PreFatigue);

public sealed record SwapRequest(string? ReplacementExerciseId, bool? ApplyToRemainingBlock);

/// <summary>A plan written by the user: one weekly routine (the training days) repeated for every week of the plan.</summary>
public sealed record ManualPlanInput(
    string? Name, string? Notes, DateOnly? StartDate, int? TotalWeeks, string? Goal, string? Experience,
    ManualDayInput[]? Days, string? TimeZone = null);

public sealed record ManualDayInput(string? Day, string? Title, ManualExerciseInput[]? Exercises);

public sealed record ManualExerciseInput(
    string? ExerciseId, int? Sets, int? MinRepetitions, int? MaxRepetitions, decimal? TargetRir, int? RestSeconds,
    string? SupersetGroup = null);
