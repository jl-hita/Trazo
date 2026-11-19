using Trazo.Domain.Planning;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>
/// Reads the observations of one muscle block by block. Deterministic counts and the rules of <see cref="LongitudinalPolicy"/>; no fitted
/// model and no confidence score. A block is only "unfavorable" when several weeks show falling performance together with fatigue or
/// sets under the prescribed repetitions; one signal alone is never enough. Soreness, body weight and other users are never read.
/// What it cannot tell, and does not pretend to: whether a muscle grew. Performance is a signal of the lifts it was measured on.
/// </summary>
public static class BlockAssessor
{
    /// <param name="blocks">The blocks of the muscle in chronological order; deload blocks are skipped.</param>
    public static IReadOnlyList<BlockSummary> Summaries(IReadOnlyList<BlockEvidence> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return blocks.Where(b => !b.IsDeload).Select(Summarize).ToList();
    }

    public static BlockSummary Summarize(BlockEvidence block)
    {
        var periods = block.Periods.Where(p => !p.IsDeload).OrderBy(p => p.Period).ToList();
        var valid = periods.Where(p => p.Quality == "valid").ToList();
        var counted = periods.Where(p => p.Quality is "valid" or "partial").ToList();
        var unfavorable = valid.Count(IsUnfavorable);
        var verdict = BlockVerdict.Insufficient;
        if (valid.Count >= LongitudinalPolicy.MinimumValidPeriods)
            verdict = unfavorable >= LongitudinalPolicy.RepeatedUnfavorablePeriods ? BlockVerdict.Unfavorable : BlockVerdict.Stable;
        return new BlockSummary(
            block.PlanId, block.Sequence, valid.Count, counted.Count - valid.Count, valid.Count(IsDeclining), unfavorable, valid.Count(IsFatigued),
            counted.Count == 0 ? 0m : Math.Round(counted.Average(p => p.PerformedSets), 1),
            counted.Count == 0 ? 0m : Math.Round(counted.Average(p => p.PrescribedSets), 1),
            periods.Count == 0 ? 0 : periods.Min(p => p.Period), periods.Count == 0 ? 0 : periods.Max(p => p.Period),
            periods.Any(p => p.PainSessions > 0), verdict);
    }

    public static bool IsDeclining(MuscleWeekObservation p) => p.PerformanceChangePercent is { } c && c <= LongitudinalPolicy.DecliningPercent;

    public static bool IsFatigued(MuscleWeekObservation p) => p.FatigueSignals >= LongitudinalPolicy.FatigueSignalsForFatiguedPeriod;

    private static decimal BelowRangeShare(MuscleWeekObservation p) => p.CompletedSets <= 0 ? 0m : p.BelowRangeSets / (decimal)p.CompletedSets;

    /// <summary>Bad period: performance down together with fatigue or with sets falling under the repetition range.</summary>
    public static bool IsUnfavorable(MuscleWeekObservation p) =>
        IsDeclining(p) && (IsFatigued(p) || BelowRangeShare(p) >= LongitudinalPolicy.BelowRangeShareForStruggle);
}
