namespace Trazo.Domain.Planning;

/// <summary>
/// The generalised profile of a plan, repeated on every anonymous research row (so a row is understandable on its own). Only closed
/// vocabularies and coarse numbers: no limitation, injury, equipment, excluded exercise, muscle priority, seed, date, time zone or
/// training day. It is copied into the research tables when an account is deleted (see docs/account-deletion.md).
/// </summary>
public sealed class EngineResearchPlanContext
{
    public string? Goal { get; set; }
    public string? Experience { get; set; }
    public string? Split { get; set; }
    public string? Readiness { get; set; }
    public string? EngineVersion { get; set; }
    /// <summary>Sessions per period, capped at 7.</summary>
    public int SessionsPerPeriod { get; set; }
    /// <summary>Plan length in periods, rounded UP to a multiple of 4 and capped at 52 (never the exact length).</summary>
    public int TotalPeriodsBand { get; set; }
}

/// <summary>
/// Anonymous copy of one <see cref="MuscleWeekObservation"/>, written when an account is deleted. It has its own random id and NO
/// user id, plan id, date or free text, and nothing maps it back to the person: that is what makes it anonymous and not just
/// pseudonymised. <see cref="SampleGroupId"/> is a random value, new for every deletion, that links the rows of one deleted plan; it
/// is only filled when the user gave research consent (see <c>TrazoUser.ResearchConsent</c>) and is not derivable from any id.
/// </summary>
public sealed class EngineResearchObservation
{
    public Guid Id { get; set; }
    public Guid? SampleGroupId { get; set; }
    public int Period { get; set; }
    public int MesocycleSequence { get; set; }
    public bool IsDeload { get; set; }
    public required string Muscle { get; set; }
    public required string Quality { get; set; }
    /// <summary>One of a closed list of codes (see <c>EngineResearchSampler.ObservationReasons</c>); null otherwise.</summary>
    public string? Reason { get; set; }
    public decimal PrescribedSets { get; set; }
    public decimal PerformedSets { get; set; }
    public int SessionsPlanned { get; set; }
    public int SessionsDone { get; set; }
    public int Exposures { get; set; }
    public int CompletedSets { get; set; }
    public int BelowRangeSets { get; set; }
    public decimal? PerformanceChangePercent { get; set; }
    public int PerformanceSamples { get; set; }
    public int FatigueSignals { get; set; }
    public int FeedbackAnswers { get; set; }
    public int LowStimulusAnswers { get; set; }
    public int DiscomfortAnswers { get; set; }
    public int PainSessions { get; set; }
    public bool ReturnFromAbsence { get; set; }
    public string? PlanGoal { get; set; }
    public string? PlanExperience { get; set; }
    public string? PlanSplit { get; set; }
    public string? PlanReadiness { get; set; }
    public string? PlanEngineVersion { get; set; }
    public int PlanSessionsPerPeriod { get; set; }
    public int PlanTotalPeriodsBand { get; set; }
}

/// <summary>
/// Anonymous copy of one <see cref="LongitudinalDecision"/>. The free-text <c>Reason</c> and the <c>SignalsJson</c> are NOT copied:
/// they are generated prose and unversioned JSON that could carry anything. Same rules as <see cref="EngineResearchObservation"/>.
/// </summary>
public sealed class EngineResearchDecision
{
    public Guid Id { get; set; }
    public Guid? SampleGroupId { get; set; }
    public int MesocycleSequence { get; set; }
    public required string Muscle { get; set; }
    public required string Action { get; set; }
    public decimal BaselineSets { get; set; }
    public decimal PreviousSets { get; set; }
    public decimal ProposedSets { get; set; }
    public int AnalyzedFromPeriod { get; set; }
    public int AnalyzedToPeriod { get; set; }
    public required string PolicyVersion { get; set; }
    public string? PlanGoal { get; set; }
    public string? PlanExperience { get; set; }
    public string? PlanSplit { get; set; }
    public string? PlanReadiness { get; set; }
    public string? PlanEngineVersion { get; set; }
    public int PlanSessionsPerPeriod { get; set; }
    public int PlanTotalPeriodsBand { get; set; }
}
