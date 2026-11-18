using Trazo.Domain.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseEquipmentRequirementsTests
{
    [Fact]
    public void EveryPreviouslyGenericMachineRequirementHasASpecificCanonicalReplacement()
    {
        var cases = new Dictionary<string, string[]>
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

        foreach (var (id, expected) in cases)
            Assert.Equal(expected, ExerciseEquipmentRequirements.Normalize(id, ["machine"]));
        Assert.Equal(18, cases.Count);
    }

    [Fact]
    public void AuditOverridesIncludeTheActualSupportsAndAttachments()
    {
        Assert.Equal(["barbell", "squat-rack"],
            ExerciseEquipmentRequirements.Normalize("inverted-row", ["barbell"]));
        Assert.Equal(["barbell", "landmine", "t-bar-row-handle"],
            ExerciseEquipmentRequirements.Normalize("t-bar-row", ["barbell"]));
        Assert.Equal(["ez-bar", "preacher-curl-bench"],
            ExerciseEquipmentRequirements.Normalize("preacher-curl", ["ez-bar", "bench"]));
        Assert.Equal(["lat-pulldown-machine"],
            ExerciseEquipmentRequirements.Normalize("lat-pulldown", ["cable"]));
        Assert.Equal(["step"],
            ExerciseEquipmentRequirements.Normalize("step-ups", ["bench"]));
    }

    [Fact]
    public void UnmappedExercisesKeepTheirExistingRequirements()
    {
        Assert.Equal(["barbell", "bench", "squat-rack"],
            ExerciseEquipmentRequirements.Normalize("close-grip-bench-press", ["barbell", "bench"]));
        Assert.Empty(ExerciseEquipmentRequirements.Normalize("bodyweight-squat", []));
    }
}
