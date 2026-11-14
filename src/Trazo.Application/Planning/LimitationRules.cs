using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Turns a body area with a limitation or injury into exercises to avoid. The catalog carries no
/// contraindication metadata, so the rules are explicit, conservative and visible: an exercise is excluded when its id
/// contains one of the listed tokens or it belongs to one of the listed movement patterns. They narrow the choice, they
/// never prescribe: the plan states it is not medical advice, and the user can exclude more exercises by id.
/// The engine still has to find an exercise for every movement pattern; when the exclusions leave a pattern empty the
/// generation fails with the engine's own explanation instead of silently dropping the pattern.
/// </summary>
public static class LimitationRules
{
    private sealed record Rule(string[] IdTokens, TrainingMovementPattern[] Patterns);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
    {
        // Deep or loaded single-leg knee flexion and impact.
        ["Knee"] = new(["lunge", "split-squat", "pistol", "step-up", "jump", "sissy", "box-squat-jump"], []),
        // Loaded overhead positions, deep dips and behind-the-neck work.
        ["Shoulder"] = new(["overhead", "ohp", "handstand", "behind-neck", "upright-row", "push-press", "z-press", "arnold", "dip", "pike", "y-raise"], []),
        // Heavy axial loading and bent-over positions.
        ["LowerBack"] = new(["deadlift", "good-morning", "rack-pull", "pendlay", "barbell-row", "t-bar-row", "zercher", "front-squat", "back-extension"], []),
        // Weight bearing on extended wrists.
        ["Wrist"] = new(["push-up", "handstand", "pike", "plank", "rollout", "burpee", "clean"], []),
        // Loaded elbow extension and deep flexion.
        ["Elbow"] = new(["skull-crusher", "tricep-extension", "overhead-tricep", "dip", "preacher", "close-grip"], [])
    };

    public static IReadOnlyList<string> Areas { get; } = Rules.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    /// <summary>The canonical name of an area ("lower back" or "lowerback" give "LowerBack"), or null.</summary>
    public static string? Canonical(string? value)
    {
        var key = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        return Rules.Keys.FirstOrDefault(area => area.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ids of the catalog exercises to avoid for the given limitations.</summary>
    public static IReadOnlyList<string> ExcludedExerciseIds(
        IEnumerable<string> limitations, IEnumerable<TrainingExerciseCandidate> catalog)
    {
        var rules = limitations.Select(Canonical).Where(area => area is not null).Distinct()
            .Select(area => Rules[area!]).ToArray();
        if (rules.Length == 0) return [];
        return catalog
            .Where(candidate => rules.Any(rule =>
                rule.IdTokens.Any(token => candidate.ExerciseId.Contains(token, StringComparison.OrdinalIgnoreCase))
                || rule.Patterns.Contains(candidate.MovementPattern)))
            .Select(candidate => candidate.ExerciseId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
