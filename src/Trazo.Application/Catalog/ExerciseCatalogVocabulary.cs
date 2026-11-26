using Trazo.Training;

namespace Trazo.Application.Catalog;

/// <summary>
/// The closed vocabularies of the exercise catalog. Patterns, muscles, goals and experience levels are the engine's own
/// enums; the rest are the values the app and the administration console know how to show.
/// </summary>
public static class ExerciseCatalogVocabulary
{
    /// <summary>The engine's patterns plus <c>Cardio</c>: cardio exercises are listed and logged, but never planned.</summary>
    public static readonly IReadOnlyList<string> MovementPatterns = [.. Enum.GetNames<TrainingMovementPattern>(), "Cardio"];
    public static readonly IReadOnlyList<string> Muscles = Enum.GetNames<TrainingMuscleGroup>();
    public static readonly IReadOnlyList<string> Goals = Enum.GetNames<TrainingGoal>();
    public static readonly IReadOnlyList<string> Experiences = Enum.GetNames<TrainingExperience>();

    public static readonly IReadOnlyList<string> Emphases = Enum.GetNames<TrainingExerciseEmphasis>();
    public static readonly IReadOnlyList<string> Techniques = Enum.GetNames<TrainingTechniqueType>().Order(StringComparer.Ordinal).ToArray();

    /// <summary>Body areas a user can declare a limitation in; each exercise declares the ones it is avoided for.</summary>
    public static readonly IReadOnlyList<string> LimitationAreas = ["Elbow", "Knee", "LowerBack", "Shoulder", "Wrist"];

    public static readonly IReadOnlyList<string> Difficulties = ["beginner", "intermediate", "advanced"];
    public static readonly IReadOnlyList<string> Mechanics = ["compound", "isolation"];
    public static readonly IReadOnlyList<string> ForceTypes = ["push", "pull", "static", "dynamic"];
    public static readonly IReadOnlyList<string> BodyParts =
        ["chest", "back", "shoulders", "upper_arms", "lower_arms", "core", "upper_legs", "lower_legs", "full_body"];

    /// <summary>
    /// Equipment a user can declare and an exercise can require. A new implement is added here together with its labels
    /// (a test keeps both in step). The generic "machine" is deliberately absent.
    /// </summary>
    public static readonly IReadOnlyList<string> Equipment =
    [
        "ab-crunch-machine", "ab-wheel", "assisted-pull-up-machine", "back-extension-machine",
        "barbell", "bench", "bicep-curl-machine", "cable", "captains-chair", "chest-fly-machine",
        "chest-press-machine", "dip-station", "dumbbell", "ez-bar", "glute-ham-developer",
        "hack-squat-machine", "hip-abduction-machine", "hip-adduction-machine", "hip-thrust-machine",
        "kettlebell", "landmine", "lat-pulldown-machine", "leg-curl-machine", "leg-extension-machine",
        "leg-press-machine", "low-row-bar", "pec-deck", "plate-loaded-lateral-raise-machine",
        "preacher-curl-bench", "pull-up-bar", "resistance-band", "reverse-fly-machine", "rower",
        "seated-calf-raise-machine", "seated-row-machine", "shoulder-press-machine", "smith-machine",
        "squat-rack", "stability-ball", "standing-calf-raise-machine", "step", "suspension-trainer",
        "t-bar-row-handle", "trap-bar"
    ];
}
