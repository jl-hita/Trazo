namespace Trazo.Application.Planning.Longitudinal;

/// <summary>How a block went. <c>Insufficient</c>: too few valid weeks to say anything. <c>Unfavorable</c>: a repeated pattern of falling
/// performance together with fatigue or sets under the range. <c>Stable</c>: enough weeks and no such pattern (it does not mean "good").</summary>
public enum BlockVerdict { Insufficient, Stable, Unfavorable }

/// <summary>Decrease: the target goes down one step. Restore: it goes back up one step towards the plan's own target (never above it).
/// Hold: the evidence was read and the target stays. Abstain: there is not enough evidence to read, the target stays.</summary>
public enum VolumeAction { Decrease, Restore, Hold, Abstain }

/// <summary>The periods of one block of one plan for one muscle, in chronological order.</summary>
public sealed record BlockEvidence(Guid PlanId, int Sequence, bool IsDeload, IReadOnlyList<Trazo.Domain.Planning.MuscleWeekObservation> Periods);

/// <summary>What the periods of one non-deload block say.</summary>
public sealed record BlockSummary(
    Guid PlanId, int Sequence, int ValidPeriods, int PartialPeriods, int DecliningPeriods, int UnfavorablePeriods, int FatiguedPeriods,
    decimal PerformedSets, decimal PrescribedSets, int FirstPeriod, int LastPeriod, bool Pain, BlockVerdict Verdict);

public sealed record VolumeDecision(
    string Muscle,
    VolumeAction Action,
    decimal BaselineSets,
    decimal PreviousSets,
    decimal ProposedSets,
    int AnalyzedFromPeriod,
    int AnalyzedToPeriod,
    string Reason,
    string SignalsJson)
{
    /// <summary>The target to hand to the generator, or null when the block should use the plain baseline.</summary>
    public decimal? LearnedTarget => ProposedSets == BaselineSets ? null : ProposedSets;
}
