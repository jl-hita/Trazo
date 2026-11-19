namespace Trazo.Domain.Planning;

/// <summary>
/// What one muscle received and how the user responded in one period (week) of a plan. It is the persisted summary that the
/// longitudinal learning reads instead of recomputing the whole workout history: a row is rebuilt (never added to) from the logs
/// of its period, so processing the same session twice cannot count it twice. These are observations, not physiological facts.
/// </summary>
public sealed class MuscleWeekObservation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>Plan week (calendar plans) or rotation (sequence plans), 1-based.</summary>
    public int Period { get; set; }
    public int MesocycleSequence { get; set; }
    public bool IsDeload { get; set; }
    /// <summary>Name of the muscle group (<c>TrainingMuscleGroup</c>).</summary>
    public required string Muscle { get; set; }
    /// <summary>valid, partial or excluded. Only valid periods can support a decision; partial ones count as half evidence.</summary>
    public required string Quality { get; set; }
    /// <summary>Why the period was excluded or partial: deload, pain, no_valid_exposure, low_completion, date_unreliable, return_from_absence.</summary>
    public string? Reason { get; set; }
    /// <summary>Effective sets the plan prescribed for the muscle in the period.</summary>
    public decimal PrescribedSets { get; set; }
    /// <summary>Effective sets really completed in valid exposures.</summary>
    public decimal PerformedSets { get; set; }
    public int SessionsPlanned { get; set; }
    public int SessionsDone { get; set; }
    /// <summary>Sessions that worked the muscle with at least two effective sets and are valid evidence.</summary>
    public int Exposures { get; set; }
    public int CompletedSets { get; set; }
    /// <summary>Completed sets under the prescribed repetition range.</summary>
    public int BelowRangeSets { get; set; }
    /// <summary>Weighted change (percent) of the performance indicator against the previous exposure of the SAME exercise; null when nothing is comparable.</summary>
    public decimal? PerformanceChangePercent { get; set; }
    public int PerformanceSamples { get; set; }
    /// <summary>Insufficient recovery, very high session effort, fatigue as the reason for not training, "loaded" before the exercise or sets skipped for fatigue.</summary>
    public int FatigueSignals { get; set; }
    public int FeedbackAnswers { get; set; }
    public int LowStimulusAnswers { get; set; }
    public int DiscomfortAnswers { get; set; }
    public int PainSessions { get; set; }
    public bool ReturnFromAbsence { get; set; }
    /// <summary>Latest registration instant among the logs this row was built from; later logs make the row stale.</summary>
    public DateTimeOffset ProcessedThrough { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void CopyFrom(MuscleWeekObservation other)
    {
        MesocycleSequence = other.MesocycleSequence; IsDeload = other.IsDeload; Quality = other.Quality; Reason = other.Reason;
        PrescribedSets = other.PrescribedSets; PerformedSets = other.PerformedSets; SessionsPlanned = other.SessionsPlanned;
        SessionsDone = other.SessionsDone; Exposures = other.Exposures; CompletedSets = other.CompletedSets;
        BelowRangeSets = other.BelowRangeSets; PerformanceChangePercent = other.PerformanceChangePercent;
        PerformanceSamples = other.PerformanceSamples; FatigueSignals = other.FatigueSignals; FeedbackAnswers = other.FeedbackAnswers;
        LowStimulusAnswers = other.LowStimulusAnswers; DiscomfortAnswers = other.DiscomfortAnswers; PainSessions = other.PainSessions;
        ReturnFromAbsence = other.ReturnFromAbsence; ProcessedThrough = other.ProcessedThrough; UpdatedAt = other.UpdatedAt;
    }
}

/// <summary>
/// Audit of what the longitudinal layer decided for one muscle when a block was generated: the weekly target before and after, the
/// signals behind it, the period analysed, the confidence and the policy version. One row per block and muscle, so generating the
/// same block twice leaves a single record.
/// </summary>
public sealed class LongitudinalDecision
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>The block the decision was applied to.</summary>
    public int MesocycleSequence { get; set; }
    public required string Muscle { get; set; }
    /// <summary>increase, decrease, hold or abstain.</summary>
    public required string Action { get; set; }
    /// <summary>The weekly target the engine would use without learning (level and priority only).</summary>
    public decimal BaselineSets { get; set; }
    /// <summary>The target used in the previous block.</summary>
    public decimal PreviousSets { get; set; }
    /// <summary>The target used in this block.</summary>
    public decimal ProposedSets { get; set; }
    /// <summary>low, medium or high.</summary>
    public required string Confidence { get; set; }
    public decimal ConfidenceScore { get; set; }
    /// <summary>First and last period analysed (0 when no period was analysed).</summary>
    public int AnalyzedFromPeriod { get; set; }
    public int AnalyzedToPeriod { get; set; }
    /// <summary>Signals used, as JSON, for audit. Never shown to the user as such.</summary>
    public required string SignalsJson { get; set; }
    /// <summary>The reason in plain language.</summary>
    public required string Reason { get; set; }
    public required string PolicyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
