using Trazo.Domain.Training;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>An exercise the engine cannot use, with the reason, so catalog gaps are visible.</summary>
public sealed record SkippedExercise(string ExerciseId, string Reason);

public sealed record CandidateMappingResult(
    IReadOnlyList<TrainingExerciseCandidate> Candidates,
    IReadOnlyList<SkippedExercise> Skipped);

/// <summary>
/// Converts catalog rows into the candidates the planning engine selects from. The catalog uses the same
/// vocabulary as the engine (movement patterns, muscle groups, goals, experience), so values are parsed, never
/// guessed: anything that does not match is dropped or the exercise is skipped and reported.
/// </summary>
public static class ExerciseCandidateMapper
{
    public static CandidateMappingResult Map(IEnumerable<ExerciseCatalogItem> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var candidates = new List<TrainingExerciseCandidate>();
        var skipped = new List<SkippedExercise>();

        foreach (var item in catalog)
        {
            if (!item.IsActive) { skipped.Add(new(item.Id, "Hidden from the catalog.")); continue; }
            if (!TryParse<TrainingMovementPattern>(item.MovementPattern, out var pattern))
            { skipped.Add(new(item.Id, $"Movement pattern '{item.MovementPattern}' is not supported by the engine.")); continue; }
            if (!TryParse<TrainingExperience>(item.MinimumExperience, out var experience))
            { skipped.Add(new(item.Id, $"Minimum experience '{item.MinimumExperience}' is not valid.")); continue; }

            var goals = ParseAll<TrainingGoal>(item.SuitableGoals);
            if (goals.Count == 0) { skipped.Add(new(item.Id, "No suitable goal the engine recognizes.")); continue; }
            var primary = ParseAll<TrainingMuscleGroup>(item.PrimaryMuscles);
            if (primary.Count == 0) { skipped.Add(new(item.Id, "No primary muscle group the engine recognizes.")); continue; }
            var secondary = ParseAll<TrainingMuscleGroup>(item.SecondaryMuscles).Where(m => !primary.Contains(m)).ToList();

            candidates.Add(new TrainingExerciseCandidate(
                ExerciseId: item.Id,
                CatalogVersion: $"{item.SeedVersion}-{item.UpdatedAt.UtcDateTime:yyyyMMddHHmm}",
                Name: item.NameEs.Length > 160 ? item.NameEs[..160] : item.NameEs,
                MovementPattern: pattern,
                RequiredEquipment: item.RequiredEquipment.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList(),
                SuitableGoals: goals,
                MinimumExperience: experience,
                SelectionPriority: Math.Clamp(item.SelectionPriority, 0, 100),
                // Imported exercises were approved and reviewed in DietoExpress and administrators curate the rest,
                // so every active catalog exercise carries reviewed metadata.
                IsReviewed: true,
                PrimaryMuscleGroups: primary,
                SecondaryMuscleGroups: secondary));
        }
        return new CandidateMappingResult(candidates, skipped);
    }

    private static bool TryParse<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result)
        && !int.TryParse(value, out _);

    private static List<T> ParseAll<T>(IEnumerable<string> values) where T : struct, Enum =>
        values.Select(v => TryParse<T>(v, out var parsed) ? (T?)parsed : null)
            .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
}
