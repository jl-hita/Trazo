namespace Trazo.Training;

/// <summary>
/// Turns the answers about each muscle (how recovered it was before training it, how much work it was) and how the lifts went into
/// volume decisions. It reads the last two sessions of a muscle. Thresholds are internal product heuristics, not clinical or
/// published rules.
/// </summary>
public static class TrainingMuscleAnswerAdapter
{
    public const int SessionsOfEvidence = 2;
    public const int MinimumSetsToCut = 3;

    /// <param name="chronological">The sessions of each muscle, oldest first, only those newer than its last change.</param>
    public static IReadOnlyList<TrainingMuscleFeedbackDecision> Evaluate(
        IReadOnlyList<TrainingMuscleSession> chronological,
        int maximumSetsPerExercise = TrainingMuscleFeedbackAdapter.MaximumSetsPerExercise)
    {
        ArgumentNullException.ThrowIfNull(chronological);
        var recent = chronological.GroupBy(s => s.Muscle, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Muscle: g.Key, Sessions: g.OrderBy(s => s.Order).TakeLast(SessionsOfEvidence).ToArray()))
            .Where(g => g.Sessions.Length == SessionsOfEvidence && g.Sessions.All(s => s.Completed))
            .ToArray();

        var decisions = new List<TrainingMuscleFeedbackDecision>();
        var overworked = recent.Where(g => g.Sessions.All(Overworked)).ToArray();
        // An exercise that also trains a muscle that has had enough never takes a set, whatever its other muscles say.
        var enough = overworked.Select(g => g.Muscle).ToHashSet(StringComparer.Ordinal);

        foreach (var (muscle, sessions) in overworked)
        {
            var pick = sessions[^1].Exercises.Where(e => e.Sets >= MinimumSetsToCut)
                .OrderByDescending(e => e.Sets).ThenBy(e => e.ExerciseId, StringComparer.Ordinal).FirstOrDefault();
            if (pick is not null)
                decisions.Add(new(TrainingMuscleFeedbackAction.ReduceSet, muscle, pick.ExerciseId,
                    $"Dos sesiones seguidas con {muscle} sin recuperar o con demasiado trabajo. Se quita una serie.",
                    // Losing performance as well does not wait for the usual pause between changes.
                    Urgent: sessions[^1].PerformanceFell));
        }

        foreach (var (muscle, sessions) in recent.Where(g => g.Sessions.All(Underworked)))
        {
            var pick = sessions[^1].Exercises
                .Where(e => e.Sets < maximumSetsPerExercise && !e.PrimaryMuscles.Any(enough.Contains))
                .OrderBy(e => e.Sets).ThenBy(e => e.ExerciseId, StringComparer.Ordinal).FirstOrDefault();
            if (pick is not null)
                decisions.Add(new(TrainingMuscleFeedbackAction.AddSet, muscle, pick.ExerciseId,
                    $"Dos sesiones seguidas con {muscle} recuperado de sobra y poco trabajo, sin perder rendimiento. Se añade una serie."));
        }

        return decisions;
    }

    private static bool Overworked(TrainingMuscleSession session) =>
        Is(session.Recovery, "notRecovered") || Is(session.Workload, "high");

    private static bool Underworked(TrainingMuscleSession session) =>
        Is(session.Recovery, "recovered") && Is(session.Workload, "low") && !session.PerformanceFell && !session.Discomfort;

    private static bool Is(string? actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One exercise of the session that trains the muscle directly.</summary>
public sealed record TrainingMuscleExercise(string ExerciseId, int Sets, IReadOnlyList<string> PrimaryMuscles);

/// <param name="Order">Chronological position of the workout.</param>
/// <param name="Recovery">recovered | justRecovered | notRecovered, asked before the first exercise of the muscle (null = not answered).</param>
/// <param name="Workload">low | adequate | high, asked after its last exercise (null = not answered).</param>
/// <param name="Completed">False for partial, skipped or deload sessions; these never count as volume evidence.</param>
/// <param name="PerformanceFell">Less was done than an earlier time with the same load in an exercise of the muscle.</param>
/// <param name="Discomfort">Mild or strong discomfort reported in any exercise that trains the muscle.</param>
public sealed record TrainingMuscleSession(
    int Order, string Muscle, string? Recovery, string? Workload, bool Completed, bool PerformanceFell, bool Discomfort,
    IReadOnlyList<TrainingMuscleExercise> Exercises);
