namespace Trazo.Domain.Training;

/// <summary>
/// Canonical physical equipment requirements for exercises whose source catalogue only
/// identifies the equipment family as "machine". Keys are stable exercise IDs.
/// </summary>
public static class ExerciseEquipmentRequirements
{
    private static readonly IReadOnlyDictionary<string, string[]> SpecificMachineRequirements =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
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
            ["v-bar-lat-pulldown"] = ["lat-pulldown-machine"]
        };

    /// <summary>Returns a precise canonical requirement for mapped machine exercises; otherwise preserves source metadata.</summary>
    public static string[] Normalize(string exerciseId, string[] sourceRequirements) =>
        SpecificMachineRequirements.TryGetValue(exerciseId, out var specific)
            ? [.. specific]
            : [.. sourceRequirements];
}
