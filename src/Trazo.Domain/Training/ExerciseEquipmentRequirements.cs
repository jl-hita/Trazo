namespace Trazo.Domain.Training;

/// <summary>
/// Canonical physical equipment requirements for reviewed exercises.
/// Values are stable IDs consumed by the equipment selector; generic "machine" is never valid.
/// </summary>
public static class ExerciseEquipmentRequirements
{
    private static readonly IReadOnlyDictionary<string, string[]> Overrides =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Exact machine models for source records that only said "machine".
            ["assisted-pull-ups"] = ["assisted-pull-up-machine"],
            ["chest-press-machine"] = ["chest-press-machine"],
            ["chest-supported-machine-row"] = ["seated-row-machine"],
            ["hip-abduction"] = ["hip-abduction-machine"],
            ["hip-adduction"] = ["hip-adduction-machine"],
            ["machine-back-extension"] = ["back-extension-machine"],
            ["machine-bicep-curl"] = ["bicep-curl-machine"],
            ["machine-calf-raise"] = ["standing-calf-raise-machine"],
            ["machine-chest-fly"] = ["chest-fly-machine"],
            ["machine-reverse-fly"] = ["reverse-fly-machine"],
            ["machine-seated-crunch"] = ["ab-crunch-machine"],
            ["machine-shoulder-press"] = ["shoulder-press-machine"],
            ["pec-deck"] = ["pec-deck"],
            ["plate-loaded-glute-drive"] = ["hip-thrust-machine"],
            ["seated-calf-raise"] = ["seated-calf-raise-machine"],
            ["single-arm-plate-loaded-lateral-raise"] = ["plate-loaded-lateral-raise-machine"],
            ["standing-calf-raise"] = ["standing-calf-raise-machine"],
            ["v-bar-lat-pulldown"] = ["lat-pulldown-machine"],

            // Equipment audit corrections: the required list describes the actual setup,
            // not merely the primary implement mentioned by the source catalogue.
            ["bent-knee-inverted-row"] = ["barbell", "squat-rack"],
            ["inverted-row"] = ["barbell", "squat-rack"],
            ["t-bar-row"] = ["barbell", "landmine", "t-bar-row-handle"],
            ["chest-supported-t-bar-row"] = ["barbell", "landmine", "t-bar-row-handle", "bench"],
            ["preacher-curl"] = ["ez-bar", "preacher-curl-bench"],
            ["front-squat"] = ["barbell", "squat-rack"],
            ["squat"] = ["barbell", "squat-rack"],
            ["bench-press"] = ["barbell", "bench", "squat-rack"],
            ["close-grip-bench-press"] = ["barbell", "bench", "squat-rack"],
            ["incline-bench-press"] = ["barbell", "bench", "squat-rack"],
            ["paused-bench-press"] = ["barbell", "bench", "squat-rack"],
            ["ohp"] = ["barbell", "squat-rack"],
            ["push-press"] = ["barbell", "squat-rack"],
            ["latpulldown"] = ["lat-pulldown-machine"],
            ["lat-pulldown"] = ["lat-pulldown-machine"],
            ["step-ups"] = ["step"]
        };

    /// <summary>
    /// Returns a precise canonical requirement for reviewed overrides; otherwise preserves
    /// the reviewed source requirements. Empty requirements are valid for true bodyweight exercises.
    /// </summary>
    public static string[] Normalize(string exerciseId, string[] sourceRequirements) =>
        Overrides.TryGetValue(exerciseId, out var specific)
            ? [.. specific]
            : [.. sourceRequirements];
}
