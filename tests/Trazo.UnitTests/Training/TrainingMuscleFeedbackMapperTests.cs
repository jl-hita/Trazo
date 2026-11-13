using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingMuscleFeedbackMapperTests
{
    private const string Prescription = """{"goal":"Hypertrophy","exercises":[{"exerciseId":"bench","sets":3,"primaryMuscleGroups":["Chest"]},{"exerciseId":"fly","sets":2,"primaryMuscleGroups":["Chest"]}]}""";
    private const string BenchFeedback = """[{"exerciseId":"bench","stimulus":"low","discomfort":"none","preFatigue":"recovered"}]""";

    [Fact]
    public void MapsNewestFirstHistoryChronologicallyAndAddsNeutralMissingExerciseExposure()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, Prescription)
        ]);

        Assert.Equal(4, exposures.Count);
        Assert.Equal(new DateOnly(2026, 11, 4), exposures[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 6), exposures[2].Date);
        Assert.Equal("bench", exposures[0].ExerciseId);
        Assert.Equal("low", exposures[0].Stimulus);
        var missing = Assert.Single(exposures, item => item.ExerciseId == "fly" && item.Date == new DateOnly(2026, 11, 6));
        Assert.Null(missing.Stimulus);
        Assert.Null(missing.Discomfort);
        Assert.Null(missing.PreFatigue);
        Assert.True(missing.SessionCompleted);
    }

    [Fact]
    public void MapperAndPlannedVolumeUseOnlyReviewedMeaningfulSecondaryStimulus()
    {
        const string rowPrescription = """{"exercises":[{"exerciseId":"seated-cable-row","sets":3,"primaryMuscleGroups":["Lats","UpperBack"],"secondaryMuscleGroups":["Biceps","Forearms"]}]}""";

        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed",
                """[{"exerciseId":"seated-cable-row","stimulus":"low","discomfort":"none","preFatigue":"recovered"}]""",
                rowPrescription)
        ]);

        var exposure = Assert.Single(exposures);
        Assert.Equal(new[] { "Biceps", "Lats", "UpperBack" }, exposure.EffectiveMuscles);

        var planned = TrainingMuscleFeedbackMapper.CountPlannedEffectiveSets([rowPrescription]);
        Assert.Equal(3, planned["Biceps"]);
        Assert.Equal(3, planned["Lats"]);
        Assert.Equal(3, planned["UpperBack"]);
        Assert.False(planned.ContainsKey("Forearms"));
    }

    [Fact]
    public void UnusableNewestPrescriptionBlocksOlderFeedbackHistory()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, null),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, Prescription)
        ]);

        Assert.Empty(exposures);
    }

    [Fact]
    public void OlderUnusablePrescriptionDoesNotReplaceNewerValidExposures()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, "not-json")
        ]);

        Assert.Equal(2, exposures.Count);
        Assert.All(exposures, item => Assert.Equal(new DateOnly(2026, 11, 6), item.Date));
    }

    [Fact]
    public void IncompleteSessionIsMarkedAndMalformedFeedbackRemainsNeutral()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "partial", "not-json", Prescription)
        ]);

        Assert.Equal(2, exposures.Count);
        Assert.All(exposures, item =>
        {
            Assert.False(item.SessionCompleted);
            Assert.Null(item.Stimulus);
            Assert.Null(item.Discomfort);
            Assert.Null(item.PreFatigue);
        });
    }
}
