namespace Trazo.Application.Planning;

// Views returned by the plan service. They only ever describe the macrocycle (phases) and the CURRENT week:
// no view contains the sessions of future weeks.

public sealed record PhaseView(int Sequence, string Name, string Type, DateOnly StartDate, DateOnly EndDate, string Objective);

public sealed record MacrocycleView(string Name, string Notes, IReadOnlyList<PhaseView> Phases);

public sealed record MesocycleView(int Sequence, string PhaseType, bool IsDeload, int StartWeek, int Weeks, int WeekInBlock);

public sealed record PlanView(
    Guid Id,
    string Status,
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
    string? SwappedFromExerciseId);

public sealed record LoggedSetView(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool Pain);

public sealed record LogView(
    Guid Id, DateTimeOffset LoggedAt, string Outcome, string NonCompletionReason, string Recovery, decimal? SessionRpe,
    bool PainReported, string? Notes, IReadOnlyList<LoggedSetView> Sets);

public sealed record SessionView(
    Guid Id, int WeekNumber, DateOnly Date, DayOfWeek Day, string Title, int EstimatedDurationMinutes, string Status,
    IReadOnlyList<ExerciseView> Exercises, LogView? Log);

public sealed record WeekView(
    Guid PlanId, int WeekNumber, DateOnly WeekStart, DateOnly WeekEnd, string Timeline, MesocycleView? Mesocycle,
    IReadOnlyList<SessionView> Sessions);

public sealed record AppliedAdaptationView(string Kind, IReadOnlyList<string> ExerciseIds, int SessionsChanged, string Explanation);

public sealed record ExerciseAdviceView(string ExerciseId, string Action, string Explanation, decimal? NextLoadKg, IReadOnlyList<int>? SuggestedRepetitions);

public sealed record AdaptationReport(
    IReadOnlyList<ExerciseAdviceView> Exercises, string SessionAction, string SessionExplanation,
    IReadOnlyList<AppliedAdaptationView> Applied, IReadOnlyList<string> Notes);

/// <summary><c>Replayed</c> is true when the session had already been logged with exactly the same data: the saved result is returned again.</summary>
public sealed record LogResult(SessionView Session, AdaptationReport Adaptation, bool Replayed = false);

public sealed record AlternativeView(
    string ExerciseId, string Name, string MovementPattern, IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> RequiredEquipment, string MatchLevel);

public sealed record AlternativesResult(string ExerciseId, IReadOnlyList<AlternativeView> Alternatives, string? Message);

public sealed record HistoryItem(
    Guid SessionId, int WeekNumber, DateOnly Date, string Title, string Outcome, DateTimeOffset LoggedAt, int CompletedSets, int PlannedSets);

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
    string? Notes, LogSetRequest[]? Sets);

public sealed record SwapRequest(string? ReplacementExerciseId, bool? ApplyToRemainingBlock);
