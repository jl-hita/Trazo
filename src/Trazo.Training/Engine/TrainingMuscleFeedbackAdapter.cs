namespace Trazo.Training;

/// <summary>
/// Turns per-exercise post-workout feedback into conservative volume decisions.
/// Thresholds are internal product heuristics, not clinical or ACSM-prescribed rules.
/// </summary>
public static class TrainingMuscleFeedbackAdapter
{
    public const int MaximumSetsPerExercise = 5;

    public static IReadOnlyList<TrainingMuscleFeedbackDecision> Evaluate(
        IReadOnlyList<TrainingMuscleFeedbackExposure> chronological,
        IReadOnlyDictionary<string, int> plannedWeeklySetsByMuscle,
        IReadOnlyDictionary<string, int> weeklyCeilingByMuscle,
        int maximumSetsPerExercise = MaximumSetsPerExercise)
    {
        ArgumentNullException.ThrowIfNull(chronological);
        ArgumentNullException.ThrowIfNull(plannedWeeklySetsByMuscle);
        ArgumentNullException.ThrowIfNull(weeklyCeilingByMuscle);
        if (maximumSetsPerExercise is < 1 or > MaximumSetsPerExercise)
            throw new ArgumentOutOfRangeException(nameof(maximumSetsPerExercise));

        var decisions = new List<TrainingMuscleFeedbackDecision>();
        var blockedExercises = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in chronological.GroupBy(item => item.ExerciseId, StringComparer.Ordinal))
        {
            var recent = group.OrderBy(item => item.Date).TakeLast(3).ToArray();
            if (recent.Length == 0) continue;
            if (EqualsValue(recent[^1].Discomfort, "strong"))
            {
                blockedExercises.Add(group.Key);
                decisions.Add(new(TrainingMuscleFeedbackAction.PauseExercise, null, group.Key,
                    "Has indicado molestias fuertes en este ejercicio. Se pausa su progresión y debe revisarlo una persona antes de seguir."));
            }
            else if (recent.Count(item => EqualsValue(item.Discomfort, "mild")) >= 2)
            {
                blockedExercises.Add(group.Key);
                decisions.Add(new(TrainingMuscleFeedbackAction.SuggestSwap, null, group.Key,
                    "Has notado molestias leves en este ejercicio en varias sesiones. Prueba una alternativa equivalente con «Cambiar ejercicio»."));
            }
        }

        // Keep blocked exercises in muscle evidence: filtering them out could hide pain and
        // incorrectly make the remaining exercises look uniformly under-stimulated.
        var byMuscle = chronological
            .SelectMany(item => item.EffectiveMuscles.Select(muscle => (Muscle: muscle, Item: item)))
            .GroupBy(pair => pair.Muscle, StringComparer.Ordinal);

        foreach (var muscleGroup in byMuscle.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var sessions = muscleGroup.GroupBy(pair => pair.Item.Date)
                .OrderBy(group => group.Key)
                .Select(group => group.Select(pair => pair.Item).ToArray()).ToArray();
            var planned = plannedWeeklySetsByMuscle.GetValueOrDefault(muscleGroup.Key);
            var ceiling = weeklyCeilingByMuscle.GetValueOrDefault(muscleGroup.Key);

            bool LowAndRecoveredWithoutDiscomfort(TrainingMuscleFeedbackExposure[] session) =>
                session.Length > 0 && session.All(item => item.SessionCompleted &&
                    EqualsValue(item.Stimulus, "low") &&
                    EqualsValue(item.PreFatigue, "recovered") &&
                    EqualsValue(item.Discomfort, "none"));

            var last2 = sessions.TakeLast(2).ToArray();
            if (last2.Length == 2 && last2.All(LowAndRecoveredWithoutDiscomfort) && planned < ceiling)
            {
                var pick = last2[^1]
                    .Where(item => !blockedExercises.Contains(item.ExerciseId) &&
                        EqualsValue(item.Stimulus, "low") && item.Sets < maximumSetsPerExercise)
                    .OrderBy(item => item.Sets).ThenBy(item => item.ExerciseId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (pick is not null)
                    decisions.Add(new(TrainingMuscleFeedbackAction.AddSet, muscleGroup.Key, pick.ExerciseId,
                        $"Dos sesiones seguidas con poco estímulo en {muscleGroup.Key}, buena recuperación y ausencia de molestias en todos los ejercicios prescritos. Se añade una serie."));
                continue;
            }

            var last3 = sessions.TakeLast(3).ToArray();
            if (last3.Length < 2 || last3[^1].Any(item => !item.SessionCompleted))
                continue;

            // Require the same exercise to report both signals in >=2 recent sessions,
            // including the latest. Never combine signals across lifts or reduce an unrelated lift.
            var candidate = last3[^1]
                .Where(item => !blockedExercises.Contains(item.ExerciseId) && item.Sets > 1 &&
                    EqualsValue(item.Stimulus, "high") && EqualsValue(item.PreFatigue, "loaded") &&
                    last3.Count(session => session.Any(exposure =>
                        exposure.ExerciseId == item.ExerciseId && exposure.SessionCompleted &&
                        EqualsValue(exposure.Stimulus, "high") && EqualsValue(exposure.PreFatigue, "loaded"))) >= 2)
                .OrderByDescending(item => item.Sets)
                .ThenBy(item => item.ExerciseId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (candidate is not null)
                decisions.Add(new(TrainingMuscleFeedbackAction.ReduceSet, muscleGroup.Key, candidate.ExerciseId,
                    $"Estímulo alto y fatiga previa repetidos en el mismo ejercicio de {muscleGroup.Key} en al menos dos de las tres últimas sesiones, incluida la última. Se quita una serie."));
        }

        return decisions;
    }

    private static bool EqualsValue(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

public enum TrainingMuscleFeedbackAction { AddSet, ReduceSet, SuggestSwap, PauseExercise }

/// <param name="Stimulus">low | adequate | high (null = not answered).</param>
/// <param name="Discomfort">none | mild | strong.</param>
/// <param name="PreFatigue">recovered | loaded.</param>
/// <param name="SessionCompleted">False for partial or not-completed sessions; these never count as volume evidence.</param>
public sealed record TrainingMuscleFeedbackExposure(
    DateOnly Date, string ExerciseId, IReadOnlyList<string> EffectiveMuscles, int Sets,
    string? Stimulus, string? Discomfort, string? PreFatigue, bool SessionCompleted = true);

public sealed record TrainingMuscleFeedbackDecision(
    TrainingMuscleFeedbackAction Action, string? Muscle, string ExerciseId, string Explanation);
