namespace Trazo.Application.Planning;

/// <summary>
/// What a user is told about load when an exercise is swapped. The engine does not convert a load between two different exercises (a dumbbell
/// press is not a fixed fraction of a barbell press: that would be an arbitrary physiological constant). It keeps continuity honestly:
/// <list type="bullet">
/// <item>the replacement starts with no load of its own, so its first session is a normal first exposure;</item>
/// <item>until the user has history with the replacement, what they did with the exercise it replaced is shown as a reference, without a proposed load;</item>
/// <item>once the replacement is logged, its own history rules (repeat, increase, estimate) and the reference disappears.</item>
/// </list>
/// The muscle evidence (feedback, volume adaptations) already follows the muscle, not the exercise.
/// </summary>
public static class SwapContinuity
{
    public const string ReferenceBasis = "reference";

    /// <summary>The reference to show for a swapped exercise, or null when it has its own history or nothing was done with the original.</summary>
    public static LastPerformanceView? Reference(LastPerformanceView? original, bool replacementHasHistory) =>
        original is null || replacementHasHistory
            ? null
            : original with { SuggestedLoadKg = null, SuggestionBasis = ReferenceBasis, BasedOnSessions = null };
}
