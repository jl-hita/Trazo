using Trazo.Domain.Planning;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>
/// Estimates how one muscle has responded to training from its observations. Deterministic and transparent: counts, ratios and the
/// rules of <see cref="LongitudinalPolicy"/>, no fitted model. A period or block is classified by several signals together; no single
/// variable (soreness is not even read) decides anything.
///
/// Signals used: performance of the same exercise against its previous exposure, share of the prescribed sets completed, sets under
/// the prescribed repetition range, fatigue signals (insufficient recovery, very high effort, fatigue as the reason for not training,
/// "loaded" before the exercise), the muscle feedback answers (stimulus, discomfort) when there are any, the regularity of the
/// training and the deloads that followed.
/// Signals NOT used: muscle soreness, body weight, any other user's data. A gain in strength is a performance signal, never proof of growth.
/// </summary>
public static class MuscleResponseEstimator
{
    /// <param name="blocks">The blocks of the muscle in chronological order (older plans first); deload blocks included.</param>
    public static MuscleResponseProfile Estimate(string muscle, IReadOnlyList<BlockEvidence> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var summaries = new List<BlockSummary>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.IsDeload) continue;
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
            summaries.Add(Summarize(block, next is { IsDeload: true } && next.PlanId == block.PlanId));
        }

        var qualified = summaries.Where(b => b.Verdict != BlockVerdict.Insufficient).ToList();
        var allPeriods = blocks.Where(b => !b.IsDeload).SelectMany(b => b.Periods).ToList();
        var valid = allPeriods.Where(p => p.Quality == "valid").ToList();
        var partial = allPeriods.Count(p => p.Quality == "partial");
        var evidence = valid.Count + partial * 0.5m;

        var observedLimit = ObservedLimit(summaries);
        var favorable = qualified.Where(b => b.Verdict == BlockVerdict.Favorable
            && (observedLimit is null || Math.Round(b.PerformedSets) < observedLimit)).ToList();
        decimal? reference = favorable.Count > 0 ? Math.Round(favorable.Max(b => b.PerformedSets)) : null;
        var tolerant = qualified.Where(b => b.Verdict != BlockVerdict.Unfavorable && b.FatiguedPeriods <= 1).ToList();
        decimal? tolerated = tolerant.Count > 0 ? Math.Round(tolerant.Max(b => b.PerformedSets)) : null;

        var last = qualified.LastOrDefault();
        var (score, feedbackCoverage, regularity) = Confidence(valid, evidence, qualified, last);
        var level = score >= LongitudinalPolicy.HighConfidence ? ConfidenceLevel.High
            : score >= LongitudinalPolicy.MediumConfidence ? ConfidenceLevel.Medium : ConfidenceLevel.Low;

        return new MuscleResponseProfile(
            muscle, valid.Count, evidence, qualified.Count,
            summaries.Count == 0 ? null : Math.Round(summaries.Where(b => b.ValidPeriods + b.PartialPeriods > 0).Select(b => b.PerformedSets).DefaultIfEmpty(0m).Average(), 1),
            summaries.Count == 0 ? null : Math.Round(summaries.Where(b => b.ValidPeriods + b.PartialPeriods > 0).Select(b => b.PrescribedSets).DefaultIfEmpty(0m).Average(), 1),
            reference, tolerated, observedLimit, TrendOf(last, valid),
            valid.Count == 0 ? 0m : Math.Round(valid.Average(p => (decimal)p.Exposures), 1),
            PreferredFrequency(valid), regularity, feedbackCoverage, score, level,
            ChangeOf(qualified), DeloadOf(blocks), summaries.TakeLast(2).Any(b => b.Pain), summaries);
    }

    /// <summary>Classifies one block: how many of its periods went well or badly, and whether that is a repeated pattern.</summary>
    public static BlockSummary Summarize(BlockEvidence block, bool followedByDeload)
    {
        var periods = block.Periods.Where(p => !p.IsDeload).OrderBy(p => p.Period).ToList();
        var valid = periods.Where(p => p.Quality == "valid").ToList();
        var counted = periods.Where(p => p.Quality is "valid" or "partial").ToList();
        var improving = valid.Count(IsImproving);
        var declining = valid.Count(IsDeclining);
        var fatigued = valid.Count(IsFatigued);
        var favorable = valid.Count(p => IsFavorable(p));
        var unfavorable = valid.Count(IsUnfavorable);
        var planned = periods.Sum(p => p.SessionsPlanned);
        var regularity = planned == 0 ? 0m : Math.Min(1m, periods.Sum(p => p.SessionsDone) / (decimal)planned);
        var performed = counted.Count == 0 ? 0m : counted.Average(p => p.PerformedSets);
        var prescribed = counted.Count == 0 ? 0m : counted.Average(p => p.PrescribedSets);

        var verdict = BlockVerdict.Insufficient;
        if (valid.Count >= LongitudinalPolicy.MinimumValidPeriods)
            verdict = unfavorable >= LongitudinalPolicy.RepeatedUnfavorablePeriods ? BlockVerdict.Unfavorable
                : unfavorable == 0 && favorable * 2 >= valid.Count ? BlockVerdict.Favorable
                : BlockVerdict.Neutral;
        return new BlockSummary(
            block.PlanId, block.Sequence, valid.Count, counted.Count - valid.Count, improving, declining, favorable, unfavorable, fatigued,
            Math.Round(performed, 1), Math.Round(prescribed, 1), Math.Round(regularity, 2),
            periods.Count == 0 ? 0 : periods.Min(p => p.Period), periods.Count == 0 ? 0 : periods.Max(p => p.Period),
            periods.Any(p => p.PainSessions > 0), followedByDeload, verdict,
            valid.Sum(p => p.FeedbackAnswers), valid.Sum(p => p.LowStimulusAnswers), valid.Sum(p => p.DiscomfortAnswers));
    }

    public static bool IsImproving(MuscleWeekObservation p) => p.PerformanceChangePercent is { } c && c >= LongitudinalPolicy.ImprovingPercent;

    public static bool IsDeclining(MuscleWeekObservation p) => p.PerformanceChangePercent is { } c && c <= LongitudinalPolicy.DecliningPercent;

    public static bool IsFatigued(MuscleWeekObservation p) =>
        p.FatigueSignals >= LongitudinalPolicy.FatigueSignalsForFatiguedPeriod;

    private static decimal Completion(MuscleWeekObservation p) => p.PrescribedSets <= 0m ? 1m : p.PerformedSets / p.PrescribedSets;

    private static decimal BelowRangeShare(MuscleWeekObservation p) => p.CompletedSets <= 0 ? 0m : p.BelowRangeSets / (decimal)p.CompletedSets;

    /// <summary>Good period: performance up, not fatigued, and the prescribed work was done.</summary>
    public static bool IsFavorable(MuscleWeekObservation p) => IsImproving(p) && !IsFatigued(p) && Completion(p) >= LongitudinalPolicy.FavorableCompletion;

    /// <summary>Bad period: performance down together with fatigue or with sets falling under the repetition range. One signal is not enough.</summary>
    public static bool IsUnfavorable(MuscleWeekObservation p) =>
        IsDeclining(p) && (IsFatigued(p) || BelowRangeShare(p) >= LongitudinalPolicy.BelowRangeShareForStruggle);

    private static decimal? ObservedLimit(IReadOnlyList<BlockSummary> summaries)
    {
        // A block that went badly, or that needed a deload after repeated fatigue, shows a volume the user did not sustain.
        var limits = summaries.Where(b => b.Verdict == BlockVerdict.Unfavorable
            || (b.FollowedByDeload && b.Verdict != BlockVerdict.Insufficient && b.FatiguedPeriods >= LongitudinalPolicy.RepeatedUnfavorablePeriods))
            .Select(b => Math.Round(b.PerformedSets)).ToList();
        return limits.Count == 0 ? null : limits.Min();
    }

    private static PerformanceTrend TrendOf(BlockSummary? last, IReadOnlyList<MuscleWeekObservation> valid)
    {
        if (last is null) return PerformanceTrend.Unknown;
        if (!valid.Any(p => p.PerformanceSamples > 0)) return PerformanceTrend.Unknown;
        if (last.DecliningPeriods > last.ImprovingPeriods) return PerformanceTrend.Declining;
        if (last.ImprovingPeriods > last.DecliningPeriods) return PerformanceTrend.Improving;
        return PerformanceTrend.Stable;
    }

    /// <summary>How the verdict changed when the volume really done changed between the last two qualified blocks.</summary>
    private static ChangeResponse ChangeOf(IReadOnlyList<BlockSummary> qualified)
    {
        if (qualified.Count < 2) return ChangeResponse.Unknown;
        var before = qualified[^2];
        var after = qualified[^1];
        if (Math.Abs(after.PerformedSets - before.PerformedSets) < 1m) return ChangeResponse.Unknown;
        var delta = Score(after.Verdict) - Score(before.Verdict);
        if (delta > 0) return ChangeResponse.Favorable;
        if (delta < 0) return ChangeResponse.Unfavorable;
        return after.Verdict == BlockVerdict.Unfavorable ? ChangeResponse.Unfavorable : ChangeResponse.Unclear;
    }

    private static int Score(BlockVerdict verdict) => verdict switch
    {
        BlockVerdict.Favorable => 2,
        BlockVerdict.Neutral => 1,
        BlockVerdict.Unfavorable => 0,
        _ => 1
    };

    /// <summary>Did the periods after a deload look better than the ones before it? Only read where both sides have valid evidence.</summary>
    private static DeloadResponse DeloadOf(IReadOnlyList<BlockEvidence> blocks)
    {
        var positive = 0;
        var negative = 0;
        var observed = 0;
        for (var i = 1; i + 1 < blocks.Count; i++)
        {
            if (!blocks[i].IsDeload) continue;
            var before = blocks[i - 1];
            var after = blocks[i + 1];
            if (before.IsDeload || after.IsDeload || before.PlanId != after.PlanId || before.PlanId != blocks[i].PlanId) continue;
            var pre = Summarize(before, followedByDeload: true);
            var post = Summarize(after, followedByDeload: false);
            if (pre.ValidPeriods == 0 || post.ValidPeriods == 0) continue;
            observed++;
            var preFatigue = pre.FatiguedPeriods / (decimal)pre.ValidPeriods;
            var postFatigue = post.FatiguedPeriods / (decimal)post.ValidPeriods;
            if (post.UnfavorablePeriods >= LongitudinalPolicy.RepeatedUnfavorablePeriods && postFatigue >= preFatigue) negative++;
            else if (postFatigue <= preFatigue && post.DecliningPeriods == 0 && post.ImprovingPeriods > 0) positive++;
        }
        if (observed == 0) return DeloadResponse.Unknown;
        if (negative > 0 && positive == 0) return DeloadResponse.Negative;
        return positive > 0 && negative == 0 ? DeloadResponse.Positive : DeloadResponse.Neutral;
    }

    /// <summary>
    /// Whether the muscle did better with two or with three or more weekly exposures. Descriptive: it needs several periods on each
    /// side and a clear difference, and it never changes a plan by itself.
    /// </summary>
    private static int? PreferredFrequency(IReadOnlyList<MuscleWeekObservation> valid)
    {
        var low = valid.Where(p => p.Exposures <= 2).ToList();
        var high = valid.Where(p => p.Exposures >= 3).ToList();
        if (low.Count < LongitudinalPolicy.MinimumPeriodsPerFrequencyBucket || high.Count < LongitudinalPolicy.MinimumPeriodsPerFrequencyBucket) return null;
        var lowShare = low.Count(IsFavorable) / (decimal)low.Count;
        var highShare = high.Count(IsFavorable) / (decimal)high.Count;
        if (Math.Abs(lowShare - highShare) < LongitudinalPolicy.FrequencyDifferenceShare) return null;
        return highShare > lowShare ? 3 : 2;
    }

    private static (decimal Score, decimal FeedbackCoverage, decimal Regularity) Confidence(
        IReadOnlyList<MuscleWeekObservation> valid, decimal evidence, IReadOnlyList<BlockSummary> qualified, BlockSummary? last)
    {
        if (valid.Count == 0) return (0m, 0m, 0m);
        var exposures = valid.Sum(p => p.Exposures);
        var feedbackCoverage = exposures == 0 ? 0m : Math.Min(1m, valid.Sum(p => p.FeedbackAnswers) / (decimal)exposures);
        var performanceCoverage = valid.Count(p => p.PerformanceSamples > 0) / (decimal)valid.Count;
        var regularity = last?.Regularity ?? Math.Min(1m, valid.Sum(p => p.SessionsDone) / (decimal)Math.Max(1, valid.Sum(p => p.SessionsPlanned)));
        var score = Math.Min(1m, evidence / LongitudinalPolicy.FullEvidencePeriods) * 0.35m
            + Math.Min(1m, qualified.Count / 2m) * 0.20m
            + regularity * 0.20m
            + feedbackCoverage * 0.15m
            + performanceCoverage * 0.10m;
        return (Math.Round(score, 2), Math.Round(feedbackCoverage, 2), Math.Round(regularity, 2));
    }
}
