using Trazo.Api.Endpoints;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseEquipmentVocabularyTests
{
    [Fact]
    public void EveryCatalogEquipmentKeyHasSpanishAndEnglishLabels()
    {
        string[] catalogKeys =
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

        foreach (var key in catalogKeys)
        {
            Assert.True(ExerciseVocabulary.Equipment.ContainsKey(key), $"Missing Spanish equipment label: {key}");
            Assert.True(ExerciseVocabulary.EquipmentEn.ContainsKey(key), $"Missing English equipment label: {key}");
            Assert.NotEqual(key, ExerciseVocabulary.Label(ExerciseVocabulary.Equipment, key));
            Assert.NotEqual(key, ExerciseVocabulary.Label(ExerciseVocabulary.EquipmentEn, key));
        }
    }

    [Fact]
    public void SpanishAndEnglishEquipmentDictionariesHaveTheSameKeys()
    {
        Assert.Equal(
            ExerciseVocabulary.Equipment.Keys.OrderBy(x => x, StringComparer.Ordinal),
            ExerciseVocabulary.EquipmentEn.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.False(ExerciseVocabulary.Equipment.ContainsKey("machine"));
        Assert.False(ExerciseVocabulary.EquipmentEn.ContainsKey("machine"));
    }
}
