namespace Trazo.Training;

/// <summary>
/// Pure search of replacement exercises for one prescribed exercise ("exercise swap").
/// It only proposes reviewed catalog exercises (or the owner's own) and never changes a plan itself.
///
/// Matching, in order of preference (internal product criteria, not literature thresholds):
///  1. Same movement pattern AND at least one shared primary muscle ("pattern" match). Ranked by the
///     share of the current primary muscles that the alternative also trains, then by curated
///     SelectionPriority. Members of the curated rotation pool are flagged.
///  2. Only when there is no pattern match: exercises that train ALL the current primary muscles
///     whatever their pattern ("muscles" match), so a patient without the usual equipment still gets
///     an option that trains the same muscles.
///  3. Nothing suitable: an explicit message, never an invented or unreviewed suggestion.
/// Goal, minimum experience and available equipment (when the caller knows it) are always respected.
/// </summary>
public static class TrainingExerciseAlternativeFinder
{
    public const int DefaultLimit = 5;
    private const int RotationPoolSize = 3;
    private const int PoolPriorityWindow = 20;

    public const string NoAlternativeMessage =
        "No hay en el catálogo ningún ejercicio revisado que pueda sustituir a este con tu nivel, objetivo y material. Consulta con tu profesional.";

    public static TrainingAlternativeResult Find(
        string currentExerciseId,
        IReadOnlyList<TrainingMuscleGroup> currentPrimaryMuscles,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlySet<string> excludedExerciseIds,
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyCollection<string>? availableEquipment = null,
        int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var current = catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.ExerciseId, currentExerciseId, StringComparison.OrdinalIgnoreCase));
        var primary = currentPrimaryMuscles.Count > 0 ? currentPrimaryMuscles : current?.PrimaryMuscleGroups ?? [];
        if (primary.Count == 0)
            return new([], NoAlternativeMessage);

        var equipment = new HashSet<string>(
            (availableEquipment ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(TrainingProgramGenerator.NormalizeEquipmentToken),
            StringComparer.OrdinalIgnoreCase);

        var eligible = catalog.Where(candidate =>
                (candidate.IsReviewed || candidate.IsOwnedByCurrentProfessional)
                && !string.Equals(candidate.ExerciseId, currentExerciseId, StringComparison.OrdinalIgnoreCase)
                && !excludedExerciseIds.Contains(candidate.ExerciseId)
                && (int)candidate.MinimumExperience <= (int)experience
                && (candidate.SuitableGoals.Contains(goal) || TrainingProgramGenerator.IsAccessoryGoalCompatible(candidate, goal))
                && (equipment.Count == 0 || TrainingProgramGenerator.HasRequiredEquipment(candidate.RequiredEquipment, equipment)))
            .ToArray();

        decimal Overlap(TrainingExerciseCandidate candidate) =>
            (decimal)primary.Count(candidate.PrimaryMuscleGroups.Contains) / primary.Count;

        var matches = new List<(TrainingExerciseCandidate Candidate, string Level)>();
        if (current is not null)
            matches.AddRange(eligible
                .Where(candidate => candidate.MovementPattern == current.MovementPattern && Overlap(candidate) > 0m)
                .Select(candidate => (candidate, "pattern")));
        if (matches.Count == 0)
            matches.AddRange(eligible
                .Where(candidate => Overlap(candidate) >= 1m
                    && candidate.MovementPattern != TrainingMovementPattern.Core)
                .Select(candidate => (candidate, "muscles")));
        if (matches.Count == 0)
            return new([], NoAlternativeMessage);

        var ranked = matches
            .OrderByDescending(item => Overlap(item.Candidate))
            .ThenByDescending(item => item.Candidate.SelectionPriority)
            .ThenBy(item => item.Candidate.ExerciseId, StringComparer.Ordinal)
            .ToArray();
        var bestPriority = ranked[0].Candidate.SelectionPriority;
        var poolIds = ranked.Take(RotationPoolSize)
            .Where(item => bestPriority - item.Candidate.SelectionPriority <= PoolPriorityWindow)
            .Select(item => item.Candidate.ExerciseId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new(ranked.Take(Math.Clamp(limit, 1, 10)).Select(item => new TrainingExerciseAlternative(
            item.Candidate.ExerciseId,
            item.Candidate.CatalogVersion,
            item.Candidate.Name,
            item.Candidate.MovementPattern.ToString(),
            item.Candidate.PrimaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray(),
            item.Candidate.SecondaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray(),
            item.Candidate.RequiredEquipment.ToArray(),
            item.Level,
            poolIds.Contains(item.Candidate.ExerciseId))).ToArray(), null);
    }
}

public sealed record TrainingExerciseAlternative(
    string ExerciseId,
    string CatalogVersion,
    string Name,
    string MovementPattern,
    IReadOnlyList<string> PrimaryMuscleGroups,
    IReadOnlyList<string> SecondaryMuscleGroups,
    IReadOnlyList<string> RequiredEquipment,
    string MatchLevel,
    bool InRotationPool);

public sealed record TrainingAlternativeResult(
    IReadOnlyList<TrainingExerciseAlternative> Alternatives, string? Message);
