namespace Trazo.Application.Planning;

// Views returned by the plan service. They only ever describe the macrocycle (phases) and the CURRENT week:
// no view contains the sessions of future weeks.

public sealed record PhaseView(
    int Sequence, string Name, string Type, DateOnly? StartDate, DateOnly? EndDate, string Objective, int StartPeriod = 0, int EndPeriod = 0);

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
    DateOnly? EndDate,
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
    string Disclaimer,
    string SchedulingMode = "Calendar",
    int SessionsPerPeriod = 0,
    IReadOnlyDictionary<string, string>? MusclePriorities = null);

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
    string? SupersetGroup = null,
    bool IsExtra = false);

/// <summary>One set of the last time the user did an exercise.</summary>
public sealed record LastSetView(int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir);

/// <summary>
/// What the user did the last time with an exercise (any plan) and a proposed working load for the next time:
/// <c>SuggestionBasis</c> is <c>repeat</c> (same load) or <c>increase</c> (every set reached the top of the range).
/// </summary>
/// <summary>
/// <c>SuggestionBasis</c> is <c>repeat</c>, <c>increase</c> or <c>estimate</c> (Premium: from the estimated maximum of the last <c>BasedOnSessions</c> sessions of the exercise).
/// </summary>
public sealed record LastPerformanceView(DateOnly Date, IReadOnlyList<LastSetView> Sets, decimal? SuggestedLoadKg, string? SuggestionBasis, int? BasedOnSessions = null);

public sealed record LoggedSetView(string ExerciseId, int SetNumber, decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool Pain);

public sealed record LogView(
    Guid Id, DateTimeOffset LoggedAt, string Outcome, string NonCompletionReason, string Recovery, decimal? SessionRpe,
    bool PainReported, string? Notes, IReadOnlyList<LoggedSetView> Sets, DateTimeOffset? PerformedAt = null, DateOnly? PerformedOn = null);

/// <summary><c>Date</c> is the planned date; <c>SequenceIndex</c> is the position of the session in the program (it never changes).</summary>
public sealed record SessionView(
    Guid Id, int WeekNumber, DateOnly? Date, DayOfWeek? Day, string Title, int EstimatedDurationMinutes, string Status,
    IReadOnlyList<ExerciseView> Exercises, LogView? Log, int SequenceIndex = 0);

public sealed record WeekView(
    Guid PlanId, int WeekNumber, DateOnly? WeekStart, DateOnly? WeekEnd, string Timeline, MesocycleView? Mesocycle,
    IReadOnlyList<SessionView> Sessions, bool Locked = false);

/// <summary>One week of the current block with its sessions (prescription and, once done, the log).</summary>
public sealed record BlockWeekView(int WeekNumber, DateOnly? WeekStart, DateOnly? WeekEnd, bool IsCurrent, IReadOnlyList<SessionView> Sessions);

/// <summary>
/// The whole current block (mesocycle, at most four weeks) so the user sees where the month is going: volume and intensity
/// per week. Later blocks are never served: they do not exist until their first week arrives.
/// </summary>
public sealed record BlockView(Guid PlanId, MesocycleView? Mesocycle, IReadOnlyList<BlockWeekView> Weeks, bool Locked = false);

/// <summary>One thing the engine did (or decided not to do) after a session, in words the user can read.</summary>
/// <param name="Kind">reduction_sets, raise_reserve, restore_sets, deload, hold_progression, muscle_add_set, muscle_reduce_set, load_update, reentry, reschedule, skip or review.</param>
/// <param name="Changed">False for a review that left the plan as it was.</param>
public sealed record AdaptationEntryView(
    Guid Id, DateTimeOffset At, string Kind, string Summary, string Explanation, IReadOnlyList<ExerciseRefView> Exercises, int SessionsChanged,
    bool Changed, string? SourceSession, DateOnly? SourceDate);

public sealed record ExerciseRefView(string ExerciseId, string Name);

/// <summary>
/// The adjustments of the current plan, newest first, with totals. <c>Scope</c> is <c>block</c> (the block in progress, the default) or <c>plan</c>
/// (every block of the active plan); the totals and the entries follow it. <c>PlanChanges</c> is the number of changes in the whole plan and
/// <c>HasEarlier</c> says there is more than the block shows. <c>Automatic</c> is false when the account has no automatic adaptation.
/// </summary>
public sealed record AdaptationHistoryView(
    int Reviews, int Changes, int SessionsAdjusted, bool Automatic, IReadOnlyList<AdaptationEntryView> Entries,
    string Scope = "block", int PlanChanges = 0, bool HasEarlier = false);

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
    Guid SessionId, int WeekNumber, DateOnly? Date, string Title, string Outcome, DateTimeOffset LoggedAt, int CompletedSets, int PlannedSets,
    DateTimeOffset? PerformedAt = null);

/// <summary>
/// Volume of one plan week, in sets. <see cref="PlannedSets"/> is null while the block of that week has not been generated
/// yet (blocks are generated when reached, so the future is never stored); <see cref="CompletedSets"/> is null for weeks
/// that have not started.
/// </summary>
public sealed record WeekLoadView(
    int Week, DateOnly? WeekStart, string? PhaseType, bool IsDeload, int? PlannedSets, int? CompletedSets, int PlannedSessions, int LoggedSessions);

/// <summary>
/// Volume of the whole plan, one entry per period, to draw planned against real load. A period is a plan week in a calendar plan and a
/// rotation (a group of sessions, not seven days) in a sequence plan: <paramref name="PeriodKind"/> says which. What the user really
/// received in time is in <paramref name="Rolling"/>.
/// </summary>
public sealed record PlanLoadView(
    Guid PlanId, int TotalWeeks, int CurrentWeek, string Unit, IReadOnlyList<WeekLoadView> Weeks, string PeriodKind = "Week",
    RollingLoadView? Rolling = null);

/// <summary>
/// The dose of the last seven real days against what the current block prescribes per seven days. <paramref name="ObservedSessionsPer7Days"/>
/// is null while there is not enough evidence of the user's pace (see <c>LoadMetrics</c>); <paramref name="NominalSessionsPer7Days"/> is what
/// they planned.
/// </summary>
public sealed record RollingLoadView(
    int WindowDays, DateTimeOffset From, DateTimeOffset To, int Workouts, int NominalSessionsPer7Days, decimal? ObservedSessionsPer7Days,
    IReadOnlyList<MuscleDoseView> Actual, IReadOnlyList<MuscleDoseView> PrescribedPer7Days);

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
    AvailabilityView? Availability, bool AvailabilityLocked, IReadOnlyList<OverdueSessionView> Overdue, bool Locked = false,
    ReentryView? Reentry = null, RescheduleOfferView? Reschedule = null, AdjustmentView? Adjustment = null, FrequencyView? Frequency = null);

/// <summary>
/// The pace the user really keeps against the plan's: <c>State</c> is Unknown, OnTrack, Below or Above; <c>Observed</c> and <c>Nominal</c> are sessions
/// per seven days; <c>SuggestedSessionsPerWeek</c> is only set when the pace is clearly lower. Informative: it changes nothing in the plan.
/// </summary>
public sealed record FrequencyView(string State, decimal? Observed, int Nominal, int? SuggestedSessionsPerWeek, string Message);

/// <summary>
/// What to do about the sessions not done, decided by <c>AdjustmentAdvisor</c> from the real state: <c>Strategy</c> is Continue, Shift, EaseBack
/// or Rebuild; <c>Reason</c> the code and <c>Message</c> the explanation. <c>CanReschedule</c> offers <c>POST plans/current/reschedule</c>;
/// <c>SuggestSkip</c> says omitting the oldest late session (<c>POST plans/current/sessions/{id}/skip</c>) is better than recovering it.
/// Omitting is always available while there is a pending session and is never done on its own.
/// </summary>
public sealed record AdjustmentView(string Strategy, string Reason, string Message, int EaseBackSessions, bool CanReschedule, bool SuggestSkip, bool CanSkip);

/// <summary>Omits a pending session on purpose: <c>Reason</c> is Time, Fatigue, Equipment, Motivation, Illness or Other.</summary>
public sealed record SkipSessionRequest(string? Reason, string? Notes);

/// <summary>Muscle to priority (Emphasize, Grow, Maintain). A muscle that is not listed is Grow; an empty map clears every priority.</summary>
public sealed record UpdateMusclePrioritiesRequest(IReadOnlyDictionary<string, string>? MusclePriorities);

/// <summary>What omitting did: the session (now Skipped), the next one, and whether the omission also adapted the plan (physical reasons, Premium).</summary>
public sealed record SkipResult(SessionView Session, bool Adapted, string Message);

/// <summary>Shown when the user has been away for a long time: advice to ease back in. The plan does not move meanwhile.</summary>
public sealed record ReentryView(int DaysSinceLastWorkout, string Message, bool Applied = false);

/// <summary>Offered in a calendar plan when the program is at least a week behind the calendar: <c>POST plans/current/reschedule</c> moves it to today.</summary>
public sealed record RescheduleOfferView(int WeeksBehind);

/// <summary>What rescheduling did: whole weeks the pending program moved forward, sessions whose date changed, and the plan's new dates.</summary>
public sealed record RescheduleResult(int WeeksShifted, int SessionsMoved, DateOnly StartDate, DateOnly EndDate);

public sealed record PlanOptions(
    IReadOnlyList<string> Goals, IReadOnlyList<string> Experience, IReadOnlyList<string> Splits,
    IReadOnlyList<string> Days, IReadOnlyList<string> Limitations, IReadOnlyList<string> Equipment,
    int MinimumTrainingDays, int MaximumTrainingDays, int MinimumSessionMinutes, int MaximumSessionMinutes,
    int MinimumTotalWeeks, int MaximumTotalWeeks, int DefaultTotalWeeks,
    IReadOnlyList<DayOption> DayOptions, string WeekStartsOn, string DefaultTimeZone,
    IReadOnlyList<string>? SchedulingModes = null, int MinimumSessionsPerPeriod = 2, int MaximumSessionsPerPeriod = 6,
    IReadOnlyList<SplitSupportOption>? SplitSupport = null);

/// <summary>
/// What a split supports: the numbers of training days it accepts in a calendar plan and the numbers of sessions per period it accepts
/// in a sequence plan. The backend owns these rules; a client should offer only these combinations.
/// </summary>
public sealed record SplitSupportOption(string Split, IReadOnlyList<int> CalendarDays, IReadOnlyList<int> SequenceSessions);

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

/// <summary>An exercise the user adds to one session. Sets, repetitions, RIR and rest are optional and default to a moderate dose.</summary>
public sealed record AddExerciseRequest(string? ExerciseId, int? Sets, int? MinRepetitions, int? MaxRepetitions, decimal? TargetRir, int? RestSeconds);

public sealed record SwapRequest(string? ReplacementExerciseId, bool? ApplyToRemainingBlock);

/// <summary>A plan written by the user: one weekly routine (the training days) repeated for every week of the plan.</summary>
public sealed record ManualPlanInput(
    string? Name, string? Notes, DateOnly? StartDate, int? TotalWeeks, string? Goal, string? Experience,
    ManualDayInput[]? Days, string? TimeZone = null, string? SchedulingMode = null);

public sealed record ManualDayInput(string? Day, string? Title, ManualExerciseInput[]? Exercises);

public sealed record ManualExerciseInput(
    string? ExerciseId, int? Sets, int? MinRepetitions, int? MaxRepetitions, decimal? TargetRir, int? RestSeconds,
    string? SupersetGroup = null);
