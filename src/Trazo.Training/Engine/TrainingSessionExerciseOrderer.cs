namespace Trazo.Training;

/// <summary>
/// Conservative, deterministic ordering pass for an already selected session.
/// It does not select/remove exercises or change prescriptions, sets, loads or weekly volume.
/// The first selected lift is fixed; main patterns stay before accessories and core stays last.
/// Within those groups, it prefers lower overlap with muscles already used, with professional
/// muscle priorities as a small tie-breaker rather than a reason to sacrifice a main lift.
/// All weights are internal product heuristics, not evidence-derived thresholds.
/// </summary>
internal static class TrainingSessionExerciseOrderer
{
    public static IReadOnlyList<TrainingExercisePrescription> Reorder(
        IReadOnlyList<TrainingExercisePrescription> exercises,
        IEnumerable<TrainingExerciseCandidate> catalog,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        if (exercises.Count < 3)
            return exercises;

        var candidates = catalog.ToDictionary(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase);
        var preferenceWeights = (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Where(x => x.Priority > 0 || x.TargetWeeklyPrimarySets.HasValue)
            .GroupBy(x => x.MuscleGroup)
            .ToDictionary(
                group => group.Key,
                group => group.Max(x => Math.Min(3m, x.Priority + (x.TargetWeeklyPrimarySets.HasValue ? 1m : 0m))));

        var original = exercises.OrderBy(x => x.Order).ToArray();
        var first = original[0];
        var main = new List<TrainingExercisePrescription>();
        var accessories = new List<TrainingExercisePrescription>();
        var core = new List<TrainingExercisePrescription>();

        foreach (var exercise in original.Skip(1))
        {
            if (candidates.TryGetValue(exercise.ExerciseId, out var candidate))
            {
                if (candidate.MovementPattern == TrainingMovementPattern.Core)
                    core.Add(exercise);
                else if (candidate.MovementPattern == TrainingMovementPattern.Accessory)
                    accessories.Add(exercise);
                else
                    main.Add(exercise);
            }
            else
            {
                // Unknown metadata is not grounds for moving an exercise ahead of known work.
                accessories.Add(exercise);
            }
        }

        var result = new List<TrainingExercisePrescription>(original.Length) { first };
        AppendGreedy(main, result, candidates, preferenceWeights);
        AppendGreedy(accessories, result, candidates, preferenceWeights);
        result.AddRange(core);

        // Avoid needless changes when the current order is already the result.
        if (result.Select(x => x.ExerciseId).SequenceEqual(original.Select(x => x.ExerciseId), StringComparer.OrdinalIgnoreCase))
            return original;

        return result.Select((exercise, index) => exercise with { Order = index + 1 }).ToArray();
    }

    private static void AppendGreedy(
        List<TrainingExercisePrescription> remaining,
        List<TrainingExercisePrescription> ordered,
        IReadOnlyDictionary<string, TrainingExerciseCandidate> candidates,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> preferenceWeights)
    {
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestScore = decimal.MinValue;
            for (var index = 0; index < remaining.Count; index++)
            {
                var exercise = remaining[index];
                if (!candidates.TryGetValue(exercise.ExerciseId, out var candidate))
                    continue;

                var score = Score(candidate, ordered, candidates, preferenceWeights);
                // Stable tie-break: retain the earliest exercise from the previous ordering.
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            ordered.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }
    }

    private static decimal Score(
        TrainingExerciseCandidate candidate,
        IReadOnlyList<TrainingExercisePrescription> ordered,
        IReadOnlyDictionary<string, TrainingExerciseCandidate> candidates,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> preferenceWeights)
    {
        var alreadyWorked = new HashSet<TrainingMuscleGroup>();
        foreach (var previous in ordered)
        {
            if (candidates.TryGetValue(previous.ExerciseId, out var previousCandidate))
            {
                alreadyWorked.UnionWith(previousCandidate.PrimaryMuscleGroups);
                alreadyWorked.UnionWith(previousCandidate.SecondaryMuscleGroups);
            }
            else
            {
                alreadyWorked.UnionWith(previous.PrimaryMuscleGroups);
                alreadyWorked.UnionWith(previous.SecondaryMuscleGroups);
            }
        }

        // Primary overlap is a stronger interference signal than secondary overlap.
        var primaryOverlap = candidate.PrimaryMuscleGroups.Count(alreadyWorked.Contains);
        var secondaryOverlap = candidate.SecondaryMuscleGroups.Count(alreadyWorked.Contains);
        var priority = candidate.PrimaryMuscleGroups.Sum(muscle =>
            preferenceWeights.TryGetValue(muscle, out var weight) ? weight : 0m);

        return -(primaryOverlap * 3m) - (secondaryOverlap * 1.25m) + priority * 0.2m;
    }
}
