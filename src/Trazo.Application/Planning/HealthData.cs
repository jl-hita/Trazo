namespace Trazo.Application.Planning;

/// <summary>
/// What in a workout log is health data (pain, discomfort, illness) and how it is left out when the person has not consented to it.
/// The workout itself is always kept: refusing it would lose what the person trained.
/// </summary>
public static class HealthData
{
    private const string Pain = "pain";
    private const string Illness = "illness";

    /// <summary>True when the log says something about the person's health. "No discomfort" is not worth telling them about.</summary>
    public static bool IsCarriedBy(ParsedLog log) =>
        log.Pain || Is(log.NonCompletionReason, Illness)
        || log.Sets.Any(s => s.Pain || Is(s.SkippedReason, Pain) || Is(s.SkippedReason, Illness))
        || log.ExerciseFeedback?.Values.Any(f => f.Discomfort is { } d && !Is(d, "none")) == true;

    /// <summary>The same log with nothing about health: the marks are cleared and the reasons become "other".</summary>
    public static ParsedLog Withhold(ParsedLog log) => log with
    {
        Pain = false,
        NonCompletionReason = Is(log.NonCompletionReason, Illness) ? "Other" : log.NonCompletionReason,
        Sets = log.Sets.Select(s => s with
        {
            Pain = false,
            SkippedReason = Is(s.SkippedReason, Pain) || Is(s.SkippedReason, Illness) ? "other" : s.SkippedReason
        }).ToList(),
        ExerciseFeedback = log.ExerciseFeedback?
            .Select(p => (p.Key, Answer: p.Value with { Discomfort = null }))
            .Where(p => p.Answer.Stimulus is not null || p.Answer.PreFatigue is not null)
            .ToDictionary(p => p.Key, p => p.Answer, StringComparer.Ordinal)
    };

    private static bool Is(string? value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
