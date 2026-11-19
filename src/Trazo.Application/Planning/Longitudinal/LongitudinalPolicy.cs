namespace Trazo.Application.Planning.Longitudinal;

/// <summary>
/// Every limit of the longitudinal learning, in one place. They are PRODUCT limits chosen to be conservative and easy to explain;
/// none of them is a physiological threshold, and none of them was measured. Changing one means bumping <see cref="Version"/>: every
/// decision records the version it was made with.
///
/// Precedence when the layers disagree about a muscle's volume (highest first): pain and safety restrictions, the user's availability
/// and restrictions, the muscle priorities, recovery and fatigue (the short-term adaptation of the current block), longitudinal
/// learning, and last the ordinary progression rules. Learning never overrides a layer above it: it only moves the weekly target of a
/// block that is being generated, inside the limits the priority planner gives (<c>TrainingMusclePriorityPlanner.Baselines</c>),
/// and the short-term adaptation still acts on the sessions of the block afterwards.
/// </summary>
public static class LongitudinalPolicy
{
    public const string Version = "longitudinal-1.2";

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
    public const decimal ImprovingPercent = 1m;
    public const decimal DecliningPercent = -2m;
    public const decimal HighSessionRpe = 9m;
    public const int FatigueSignalsForFatiguedPeriod = 2;
    public const decimal BelowRangeShareForStruggle = 0.3m;
    public const decimal FavorableCompletion = 0.9m;

    // ---- Evidence needed
    /// <summary>Valid periods of a block needed before it can say anything.</summary>
    public const int MinimumValidPeriods = 3;
    /// <summary>Unfavorable periods of a block needed before it counts as a repeated pattern (one bad week is never enough).</summary>
    public const int RepeatedUnfavorablePeriods = 2;
    /// <summary>Share of the prescribed volume the user must have really done for a hold or an increase to be justified.</summary>
    public const decimal MinimumDoseAdherence = 0.85m;
    public const decimal MinimumRegularity = 0.7m;
    public const decimal MediumConfidence = 0.5m;
    public const decimal HighConfidence = 0.75m;
    /// <summary>Evidence periods (valid = 1, partial = half) that give the full weight to the amount-of-evidence part of the confidence.</summary>
    public const decimal FullEvidencePeriods = 8m;
    public const int MinimumFeedbackAnswers = 3;
    public const decimal LowStimulusShare = 0.5m;
    public const int DiscomfortAnswersToHold = 2;

    // ---- Consolidating a change of the individual reference (product criteria, not physiological values)
    /// <summary>
    /// Consecutive favorable blocks needed before the weekly reference of a muscle goes up. One favorable block is a sign, two are a
    /// repeated pattern; this only governs the learned reference. Ordinary short-term adaptation (loads, fatigue, deloads, pain) never waits for it.
    /// </summary>
    public const int FavorableBlocksToIncrease = 2;
    /// <summary>Confidence an increase needs, above the medium level: about 0.55 is "some evidence", not a basis to raise the reference.</summary>
    public const decimal IncreaseMinimumConfidence = 0.65m;

    // ---- Size of a change
    /// <summary>Largest change of the weekly target between two consecutive blocks.</summary>
    public const decimal StepSets = 1m;
    public const decimal MaximumAboveBaselineSets = 3m;
    public const decimal MaximumBelowBaselineSets = 3m;

    // ---- Carrying what was learned into a new plan (cross-plan learning; product criteria, not physiological values)
    /// <summary>Days after the last evidence of a muscle during which the history keeps its full weight (a normal gap between two plans).</summary>
    public const int CarryOverFullWeightDays = 56;
    /// <summary>Days after the last evidence at which the history no longer says anything: the new plan starts from its own baseline.</summary>
    public const int CarryOverExpiryDays = 240;
    /// <summary>Largest increase over the baseline a new plan may start with. Smaller than <see cref="MaximumAboveBaselineSets"/>: the old plan earned it in other conditions.</summary>
    public const decimal MaximumCarriedIncreaseSets = 1m;

    // ---- Frequency evidence (descriptive; it does not change a plan by itself)
    public const int MinimumPeriodsPerFrequencyBucket = 3;
    public const decimal FrequencyDifferenceShare = 0.4m;
}
