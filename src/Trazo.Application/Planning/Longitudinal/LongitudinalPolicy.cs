namespace Trazo.Application.Planning.Longitudinal;

/// <summary>
/// Every limit of the longitudinal layer, in one place. They are PRODUCT limits chosen to be conservative and easy to explain;
/// none of them is a physiological threshold and none was measured. Changing one means bumping <see cref="Version"/>: every
/// decision records the version it was made with.
///
/// What this layer does, and what it deliberately does not do:
/// - It records, per muscle and per week, what was prescribed, what was done and which unfavourable signals appeared (observations).
/// - It can LOWER the weekly target of a muscle when repeated weeks show falling performance together with fatigue or sets under the
///   repetition range, and it gives the reduction back (never above the plan's own target) when a later block no longer shows that.
/// - It never RAISES a weekly target above what the plan itself prescribes. A better performance on an exercise is not evidence that
///   the muscles it trains respond to more volume, so nothing is inferred from it.
/// - Pain always prevails: while pain is recorded in the recent blocks nothing is given back and nothing is learned from those sessions.
/// The weekly volume itself (the baseline) is decided by the priority planner and the allocator; this layer cannot move it outside the
/// band between the baseline and <see cref="MaximumBelowBaselineSets"/> sets under it (never under the baseline's floor).
/// </summary>
public static class LongitudinalPolicy
{
    public const string Version = "longitudinal-2.0";

    // ---- Quality of an exposure
    /// <summary>A muscle worked with fewer effective sets than this in a session did not get a real exposure (same limit as <c>LoadMetrics</c>).</summary>
    public const decimal MeaningfulSets = 2m;
    /// <summary>More days than this without any workout make the next one a re-entry: it counts for volume but not for performance or fatigue.</summary>
    public const int AbsenceDays = 14;
    /// <summary>A workout registered this many days after it happened has an unreliable date: it is not used to learn.</summary>
    public const int LateRegistrationDays = 14;
    /// <summary>Two exposures of the same exercise further apart than this are not compared.</summary>
    public const int ComparableGapDays = 28;
    /// <summary>A period in which less than this share of the prescribed sets was done is partial evidence.</summary>
    public const decimal PartialCompletionRatio = 0.6m;

    // ---- Reading a period
    public const decimal DecliningPercent = -2m;
    public const decimal HighSessionRpe = 9m;
    public const int FatigueSignalsForFatiguedPeriod = 2;
    public const decimal BelowRangeShareForStruggle = 0.3m;

    // ---- Evidence needed
    /// <summary>Valid periods of a block needed before it can say anything.</summary>
    public const int MinimumValidPeriods = 3;
    /// <summary>Unfavourable periods of a block needed before it counts as a repeated pattern (one bad week is never enough).</summary>
    public const int RepeatedUnfavorablePeriods = 2;

    // ---- Size of a change
    /// <summary>Largest change of the weekly target between two consecutive blocks.</summary>
    public const decimal StepSets = 1m;
    public const decimal MaximumBelowBaselineSets = 3m;
}
