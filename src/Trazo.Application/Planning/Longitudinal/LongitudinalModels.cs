namespace Trazo.Application.Planning.Longitudinal;

public enum ConfidenceLevel { Low, Medium, High }

public enum PerformanceTrend { Unknown, Improving, Stable, Declining }

/// <summary>How a block went, read from its periods. Insufficient means there were not enough valid periods to say.</summary>
public enum BlockVerdict { Insufficient, Favorable, Neutral, Unfavorable }

public enum ChangeResponse { Unknown, Favorable, Unclear, Unfavorable }

public enum DeloadResponse { Unknown, Positive, Neutral, Negative }

public enum VolumeAction { Increase, Decrease, Hold, Abstain }

/// <summary>The periods of one block of one plan for one muscle, in chronological order of blocks (plan, then sequence).</summary>
public sealed record BlockEvidence(Guid PlanId, int Sequence, bool IsDeload, IReadOnlyList<Trazo.Domain.Planning.MuscleWeekObservation> Periods);

/// <summary>What the periods of one non-deload block say.</summary>
public sealed record BlockSummary(
    Guid PlanId, int Sequence, int ValidPeriods, int PartialPeriods, int ImprovingPeriods, int DecliningPeriods,
    int FavorablePeriods, int UnfavorablePeriods, int FatiguedPeriods, decimal PerformedSets, decimal PrescribedSets,
    decimal Regularity, int FirstPeriod, int LastPeriod, bool Pain, bool FollowedByDeload, BlockVerdict Verdict,
    int FeedbackAnswers = 0, int LowStimulusAnswers = 0, int DiscomfortAnswers = 0)
{
    /// <summary>Share of the prescribed volume really done (1 when nothing was prescribed).</summary>
    public decimal Adherence => PrescribedSets <= 0m ? 1m : PerformedSets / PrescribedSets;
}

/// <summary>
/// The estimate of how one muscle responds to training, derived from its observations. Everything in it is an estimate from
/// imperfect data, not a measured physiological fact; <see cref="Confidence"/> says how much data supports it.
/// </summary>
public sealed record MuscleResponseProfile(
    string Muscle,
    int ValidPeriods,
    decimal EvidencePeriods,
    int ValidBlocks,
    decimal? AveragePerformedSets,
    decimal? AveragePrescribedSets,
    /// <summary>Weekly volume really done in the best-supported favorable block (below any observed limit); null when no block was favorable.</summary>
    decimal? ReferenceSets,
    /// <summary>Highest weekly volume really done in a block without repeated fatigue or deterioration.</summary>
    decimal? ToleratedSets,
    /// <summary>Lowest weekly volume at which repeated fatigue and deterioration (or a deload after fatigue) was observed.</summary>
    decimal? ObservedLimitSets,
    PerformanceTrend Trend,
    decimal ActualFrequency,
    int? PreferredFrequency,
    decimal Regularity,
    decimal FeedbackCoverage,
    decimal ConfidenceScore,
    ConfidenceLevel Confidence,
    ChangeResponse VolumeChangeResponse,
    DeloadResponse DeloadResponse,
    bool PainInRecentBlocks,
    IReadOnlyList<BlockSummary> Blocks)
{
    /// <summary>The most recent non-deload block with enough valid periods, or null.</summary>
    public BlockSummary? LastQualifiedBlock => Blocks.LastOrDefault(b => b.Verdict != BlockVerdict.Insufficient);
}

public sealed record VolumeDecision(
    string Muscle,
    VolumeAction Action,
    decimal BaselineSets,
    decimal PreviousSets,
    decimal ProposedSets,
    ConfidenceLevel Confidence,
    decimal ConfidenceScore,
    int AnalyzedFromPeriod,
    int AnalyzedToPeriod,
    string Reason,
    string SignalsJson)
{
    /// <summary>The target to hand to the generator, or null when the block should use the plain baseline.</summary>
    public decimal? LearnedTarget => ProposedSets == BaselineSets ? null : ProposedSets;
}
