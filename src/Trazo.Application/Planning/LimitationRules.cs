using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Turns a body area with a limitation or injury into exercises to avoid. The catalog carries no contraindication
/// metadata, so the rules are explicit, conservative and visible. Each area combines two kinds of rule:
/// structural ones, read from the exercise's reviewed metadata (movement pattern, primary muscles, equipment), which
/// also cover exercises added to the catalog later, and id tokens for movements the metadata cannot describe
/// (a dip, a skull crusher, a behind-the-neck press).
/// They narrow the choice, they never prescribe: the plan states it is not medical advice, and the user can exclude
/// more exercises by id.
/// An area never makes a plan impossible on its own: when the exclusions leave overhead pressing or single-leg work
/// without any exercise the engine keeps or substitutes the sibling pattern (horizontal press, hinge) and says so in
/// the plan's warnings. Squat, hinge, horizontal press and row are never dropped: a profile left without one of them
/// still fails with the engine's explanation.
/// </summary>
public static class LimitationRules
{
    private sealed record Rule(string[] IdTokens, Func<TrainingExerciseCandidate, bool> Structural, string[] ExceptTokens);

    private static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
    {
        // Deep or loaded single-leg knee flexion, impact and knee-over-toe isolation.
        ["Knee"] = new(
            ["lunge", "split-squat", "pistol", "step-up", "jump", "sissy", "reverse-nordic", "skater", "burpee"],
            _ => false,
            []),
        // The arm pressed or raised overhead under load, whatever the implement: every vertical press, direct
        // deltoid raises (lateral, frontal, Y), overhead triceps work, dips and upright rows.
        // Rear-deltoid and scapular work (reverse fly, face pull, pull-apart) stays: it is horizontal pulling.
        ["Shoulder"] = new(
            ["overhead", "ohp", "handstand", "behind-neck", "upright-row", "push-press", "z-press", "arnold", "dip",
             "pike", "y-raise", "pullover", "snatch", "jerk"],
            candidate => candidate.MovementPattern == TrainingMovementPattern.VerticalPush ||
                (candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                 candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Deltoids) &&
                 HasToken(candidate, "raise")),
            []),
        // Heavy axial loading and unsupported bent-over positions: deadlifts and good mornings, barbell squats and
        // bent-over barbell rows. Chest-supported rows take the load off the spine and stay.
        ["LowerBack"] = new(
            ["deadlift", "good-morning", "rack-pull", "pendlay", "barbell-row", "bent-over", "t-bar-row", "zercher",
             "front-squat", "back-extension", "hyperextension", "clean", "snatch"],
            candidate => RequiresAny(candidate, "barbell", "trap-bar", "safety-bar") &&
                candidate.MovementPattern is TrainingMovementPattern.Squat or TrainingMovementPattern.HorizontalPull,
            ["chest-supported"]),
        // Weight bearing on extended wrists (bodyweight pressing on the hands, planks, rollouts, dips), the front
        // rack position and straight-bar curls.
        ["Wrist"] = new(
            ["push-up", "handstand", "pike", "plank", "rollout", "burpee", "clean", "dip", "front-squat",
             "barbell-curl", "reverse-curl", "wrist-curl", "upright-row", "zercher"],
            candidate => IsBodyweight(candidate) &&
                candidate.MovementPattern is TrainingMovementPattern.HorizontalPush or TrainingMovementPattern.VerticalPush,
            []),
        // Loaded elbow extension in deep flexion or overhead, dips, close-grip pressing, and elbow flexion fixed in a
        // stretched or fully supinated position (preacher, incline and straight-bar curls, chin-ups).
        // Pushdowns and neutral curls stay.
        ["Elbow"] = new(
            ["skull-crusher", "tricep-extension", "triceps-extension", "overhead-tricep", "dip", "preacher",
             "close-grip", "barbell-curl", "incline-db-curl", "chin-up", "weighted-pull-up", "french-press", "kickback"],
            candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Triceps) &&
                (HasToken(candidate, "overhead") || HasToken(candidate, "extension")),
            [])
    };

    private static readonly HashSet<string> SupportEquipment = new(StringComparer.OrdinalIgnoreCase)
    {
        "bench", "box", "step", "mat", "wall", "chair"
    };

    private static bool HasToken(TrainingExerciseCandidate candidate, string token) =>
        candidate.ExerciseId.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool RequiresAny(TrainingExerciseCandidate candidate, params string[] equipment) =>
        candidate.RequiredEquipment.Select(PlanProfileParser.NormalizeToken)
            .Any(token => equipment.Contains(token, StringComparer.OrdinalIgnoreCase));

    /// <summary>No implement at all, or only something to lean on: the hands carry the body weight.</summary>
    private static bool IsBodyweight(TrainingExerciseCandidate candidate) =>
        candidate.RequiredEquipment.Select(PlanProfileParser.NormalizeToken).All(SupportEquipment.Contains);

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
            .Where(candidate => rules.Any(rule => Excludes(rule, candidate)))
            .Select(candidate => candidate.ExerciseId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Excludes(Rule rule, TrainingExerciseCandidate candidate)
    {
        if (rule.ExceptTokens.Any(token => HasToken(candidate, token))) return false;
        return rule.IdTokens.Any(token => HasToken(candidate, token)) || rule.Structural(candidate);
    }
}
