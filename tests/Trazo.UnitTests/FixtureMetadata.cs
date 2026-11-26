using Trazo.Application.Planning;
using Trazo.Domain.Training;
using Trazo.Training;

namespace Trazo.UnitTests;

/// <summary>
/// Planning metadata for the hand written exercises of the tests. The production code reads it from the catalog; tests build
/// small catalogs with familiar ids, and this gives them what the exercise seed would say about an exercise of that name (the
/// conventions the metadata was first written from). It is test data only.
/// </summary>
internal static class FixtureMetadata
{
    private static readonly IReadOnlyDictionary<string, TrainingMuscleGroup[]> MeaningfulSecondary =
        new Dictionary<string, TrainingMuscleGroup[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Horizontal/vertical pressing: elbow extension is a meaningful loaded action.
            ["bench-press"] = [TrainingMuscleGroup.Triceps],
            ["db-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["incline-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["incline-db-press"] = [TrainingMuscleGroup.Triceps],
            ["smith-machine-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["smith-machine-incline-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["chest-press-machine"] = [TrainingMuscleGroup.Triceps],
            ["push-up"] = [TrainingMuscleGroup.Triceps],
            ["knee-push-ups"] = [TrainingMuscleGroup.Triceps],
            ["incline-push-ups"] = [TrainingMuscleGroup.Triceps],
            ["ohp"] = [TrainingMuscleGroup.Triceps],
            ["dumbbell-shoulder-press"] = [TrainingMuscleGroup.Triceps],
            ["machine-shoulder-press"] = [TrainingMuscleGroup.Triceps],

            // Rows load elbow flexors and lats dynamically; vertical pulls load elbow flexors.
            ["barbell-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["single-arm-db-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-db-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-machine-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["t-bar-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-kettlebell-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["kneeling-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["one-arm-seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["v-bar-lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["assisted-pull-ups"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["pull-up"] = [TrainingMuscleGroup.Biceps],
            ["chin-ups"] = [TrainingMuscleGroup.Biceps],

            // Knee-dominant compounds: both quads and glutes can receive meaningful dynamic work.
            ["squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["front-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["goblet-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["db-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["leg-press"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["horizontal-leg-press"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["hack-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bulgarian-split-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["reverse-lunge"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["step-ups"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],

            // Hip hinges: hamstrings and glutes are loaded through hip extension.
            ["deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hex-bar-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["romanian-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["dumbbell-romanian-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],

            // Rear-delt fly/face-pull variations dynamically load the upper back as well.
            ["dumbbell-reverse-fly"] = [TrainingMuscleGroup.UpperBack],
            ["machine-reverse-fly"] = [TrainingMuscleGroup.UpperBack],
            ["face-pull"] = [TrainingMuscleGroup.UpperBack],

            // Hip-extension back extensions and loaded knee raises also train these muscles dynamically.
            ["machine-back-extension"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hanging-knee-raise"] = [TrainingMuscleGroup.HipFlexors],
            ["captains-chair-knee-raise"] = [TrainingMuscleGroup.HipFlexors],

            // TRAZO additions. Same criteria as above: a secondary muscle counts only when the exercise loads it
            // dynamically through a meaningful range of motion.
            ["wide-grip-seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-t-bar-row"] = [TrainingMuscleGroup.Biceps],
            ["pendlay-row"] = [TrainingMuscleGroup.Biceps],
            ["weighted-pull-up"] = [TrainingMuscleGroup.Biceps],
            ["arnold-press"] = [TrainingMuscleGroup.Triceps],
            ["seated-barbell-overhead-press"] = [TrainingMuscleGroup.Triceps],
            ["push-press"] = [TrainingMuscleGroup.Triceps],
            ["z-press"] = [TrainingMuscleGroup.Triceps],
            ["pike-push-up"] = [TrainingMuscleGroup.Triceps],
            ["handstand-push-up"] = [TrainingMuscleGroup.Triceps],
            ["paused-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["pause-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["zercher-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["barbell-bulgarian-split-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["pistol-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["rack-pull"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["deficit-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["good-morning"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hanging-leg-raise"] = [TrainingMuscleGroup.HipFlexors],
            ["bodyweight-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bodyweight-reverse-lunge"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bent-knee-inverted-row"] = [TrainingMuscleGroup.Biceps],
            ["inverted-row"] = [TrainingMuscleGroup.Biceps], // same movement as the bent-knee version, audited with it
            ["negative-pull-up"] = [TrainingMuscleGroup.Biceps],
            ["band-row"] = [TrainingMuscleGroup.Biceps],
            ["band-lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["band-shoulder-press"] = [TrainingMuscleGroup.Triceps],
            ["elevated-pike-push-up"] = [TrainingMuscleGroup.Triceps],
            ["dumbbell-bent-over-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["smith-machine-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps]
        };

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

    public static List<TrainingMuscleGroup> Effective(string id, IEnumerable<TrainingMuscleGroup> secondary) =>
        MeaningfulSecondary.TryGetValue(id, out var meaningful) ? secondary.Where(meaningful.Contains).ToList() : [];

    private static bool IsLateral(string id, IEnumerable<TrainingMuscleGroup> primary) =>
        primary.Contains(TrainingMuscleGroup.Deltoids) && id.Contains("lateral-raise", StringComparison.OrdinalIgnoreCase);

    private static List<string> AreasOf(TrainingExerciseCandidate candidate) => Rules.Keys
        .Where(area => !Rules[area].ExceptTokens.Any(token => HasToken(candidate, token)) &&
            (Rules[area].IdTokens.Any(token => HasToken(candidate, token)) || Rules[area].Structural(candidate)))
        .Order(StringComparer.Ordinal).ToList();

    public static TrainingExerciseCandidate Described(this TrainingExerciseCandidate candidate) => candidate with
    {
        EffectiveSecondaryMuscleGroups = Effective(candidate.ExerciseId, candidate.SecondaryMuscleGroups),
        Emphasis = IsLateral(candidate.ExerciseId, candidate.PrimaryMuscleGroups) ? [TrainingExerciseEmphasis.LateralDeltoid] : [],
        LimitationAreas = AreasOf(candidate)
    };

    public static TrainingExercisePrescription Described(this TrainingExercisePrescription exercise) => exercise with
    {
        EffectiveSecondaryMuscleGroups = Effective(exercise.ExerciseId, exercise.SecondaryMuscleGroups)
    };

    public static ExerciseCatalogItem Described(this ExerciseCatalogItem item)
    {
        var candidate = ExerciseCandidateMapper.Map([item]).Candidates.SingleOrDefault();
        if (candidate is null) return item;
        var described = candidate.Described();
        item.EffectiveSecondaryMuscles = described.EffectiveSecondaryMuscleGroups!.Select(m => m.ToString()).ToArray();
        item.Emphasis = described.Emphasis!.Select(e => e.ToString()).ToArray();
        item.LimitationAreas = [.. described.LimitationAreas!];
        return item;
    }
}
