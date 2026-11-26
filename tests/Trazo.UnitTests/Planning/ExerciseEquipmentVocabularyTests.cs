using Trazo.Api.Endpoints;
using Trazo.Application.Catalog;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseEquipmentVocabularyTests
{
    [Fact]
    public void EveryCatalogEquipmentKeyHasSpanishAndEnglishLabels()
    {
        foreach (var key in ExerciseCatalogVocabulary.Equipment)
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
