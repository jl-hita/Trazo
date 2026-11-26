using Trazo.Application.Catalog;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Turns a body area with a limitation or injury into exercises to avoid. Which areas an exercise is avoided for is reviewed
/// catalog data (<c>limitationAreas</c>), so an exercise added later is covered by declaring them, never by its name.
/// They narrow the choice, they never prescribe: the plan states it is not medical advice, and the user can exclude
/// more exercises by id. An exercise nobody has reviewed has no trustworthy declaration, so any limitation avoids it.
/// An area never makes a plan impossible on its own: when the exclusions leave overhead pressing or single-leg work
/// without any exercise the engine keeps or substitutes the sibling pattern (horizontal press, hinge) and says so in
/// the plan's warnings. Squat, hinge, horizontal press and row are never dropped: a profile left without one of them
/// still fails with the engine's explanation.
/// </summary>
public static class LimitationRules
{
    public static IReadOnlyList<string> Areas { get; } = ExerciseCatalogVocabulary.LimitationAreas;

    /// <summary>The canonical name of an area ("lower back" or "lowerback" give "LowerBack"), or null.</summary>
    public static string? Canonical(string? value)
    {
        var key = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        return Areas.FirstOrDefault(area => area.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ids of the catalog exercises to avoid for the given limitations.</summary>
    public static IReadOnlyList<string> ExcludedExerciseIds(
        IEnumerable<string> limitations, IEnumerable<TrainingExerciseCandidate> catalog)
    {
        var areas = limitations.Select(Canonical).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (areas.Count == 0) return [];
        return catalog
            .Where(candidate => !(candidate.IsReviewed || candidate.IsOwnedByCurrentProfessional) ||
                (candidate.LimitationAreas?.Any(areas.Contains) ?? false))
            .Select(candidate => candidate.ExerciseId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
