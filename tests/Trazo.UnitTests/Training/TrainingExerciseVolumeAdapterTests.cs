using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingExerciseVolumeAdapterTests
{
    [Fact]
    public void RepeatedExerciseSpecificFatigueProposesOneSetReduction()
    {
        var history = new[]
        {
            Exposure("bench"),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench", skippedReason: "fatigue")
        };

        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(history));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendation.Action);
        Assert.Equal(2, recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void ReducedVolumeIsNotRestoredWhenRecentRirDataIsMissing()
    {
        var history = Enumerable.Range(0, 3)
            .Select(_ => new TrainingExerciseVolumeExposure(
                "bench", 2, 2, SessionCompleted: true,
                TargetRir: 2m, ActualAverageRir: null, OriginalPlannedSets: 3))
            .ToArray();

        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(history));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void ReducedVolumeIsRestoredOnlyAfterThreeCompleteExposuresMeetTargetRir()
    {
        var history = Enumerable.Range(0, 3)
            .Select(_ => new TrainingExerciseVolumeExposure(
                "bench", 2, 2, SessionCompleted: true,
                TargetRir: 2m, ActualAverageRir: 2m, OriginalPlannedSets: 3))
            .ToArray();

        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(history));

        Assert.Equal(TrainingExerciseVolumeAction.ProposeRestoreOneSet, recommendation.Action);
        Assert.Equal(3, recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void TwoFatigueExposuresAreInsufficientWithoutAThirdExposure()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench", skippedReason: "fatigue")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
        Assert.Equal(2, recommendation.ExposuresAnalyzed);
    }

    [Fact]
    public void OneDifficultExposureDoesNotTriggerVolumeReduction()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench"),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("equipment")]
    [InlineData("illness")]
    [InlineData("motivation")]
    public void NonFatigueReasonsDoNotReduceExerciseVolume(string reason)
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: reason),
            Exposure("bench", skippedReason: reason),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void PainAlwaysPausesAndIsNotTreatedAsOrdinaryFatigue()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench"),
            Exposure("bench", pain: true),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.PauseAndReview, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void TwoRirShortfallsCanTriggerReviewButOneExposureCannotCountTwice()
    {
        var oneExposure = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue", actualRir: 0m),
            Exposure("bench"),
            Exposure("bench")
        ]));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, oneExposure.Action);

        var repeated = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench"),
            Exposure("bench", actualRir: 0m),
            Exposure("bench", actualRir: 0m)
        ]));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, repeated.Action);
    }

    [Fact]
    public void SingleSetExerciseIsReviewedButNeverReducedBelowOne()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("plank", planned: 1, skippedReason: "fatigue"),
            Exposure("plank", planned: 1, skippedReason: "fatigue"),
            Exposure("plank", planned: 1, skippedReason: "fatigue")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.ReviewWithoutReduction, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void IncompleteSessionDoesNotCountAsExerciseFatigueEvidence()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue", completedSession: false),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void MapperIgnoresValidJsonWithUnexpectedExerciseShape()
    {
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", """["legacy-string"]""", """[]""")
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Empty(exposures);
    }

    [Fact]
    public void MapperRejectsFeedbackWhoseExerciseIdOrPlannedSetsDoNotMatchPrescription()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":2,"completedSets":2,"sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map([
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        ]);

        var unknown = Assert.Single(exposures);
        Assert.Equal("bench", unknown.ExerciseId);
        Assert.Equal(0, unknown.CompletedWorkingSets);
        Assert.Null(unknown.SkippedSetReason);
    }

    [Fact]
    public void PartialSessionDoesNotCountAsCompletedExerciseFatigueEvidence()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":false,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";
        // Mapper input mirrors the SQL query: newest record first.
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("partial", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("partial", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        Assert.True(exposures[0].SessionCompleted);
        Assert.False(exposures[1].SessionCompleted);
        Assert.False(exposures[2].SessionCompleted);
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void MissingRirOnAnyCompletedWorkingSetDoesNotCreateAnAverage()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":2,"completedSets":2,"sets":[{"kind":"working","completed":true,"rir":0},{"kind":"working","completed":true,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":2,"targetRepetitionsInReserve":2}]}""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.All(exposures, exposure => Assert.Null(exposure.ActualAverageRir));
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void FeedbackMapperReadsStableExerciseIdAndRirButDoesNotSpreadGlobalPain()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":false,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        Assert.All(exposures, exposure =>
        {
            Assert.Equal("bench", exposure.ExerciseId);
            Assert.Equal(2m, exposure.TargetRir);
            Assert.Equal(1m, exposure.ActualAverageRir);
            Assert.Equal("fatigue", exposure.SkippedSetReason);
            Assert.False(exposure.PainReported);
        });
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendation.Action);
    }


    [Fact]
    public void MissingExerciseFeedbackCreatesNeutralExposureAndPreventsStaleFatigueReduction()
    {
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";
        const string fatigue = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1}]}]""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", null, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        Assert.Equal(0, exposures[^1].CompletedWorkingSets);
        Assert.Null(exposures[^1].SkippedSetReason);
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void MapperCreatesNeutralExposureForPrescribedExerciseOmittedFromFeedback()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":3,"sets":[{"kind":"working","completed":true,"rir":2},{"kind":"working","completed":true,"rir":2},{"kind":"working","completed":true,"rir":2}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2},{"exerciseId":"row","sets":2,"targetRepetitionsInReserve":2}]}""";

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map([
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        ]);

        Assert.Equal(2, exposures.Count);
        var row = exposures.Single(exposure => exposure.ExerciseId == "row");
        Assert.Equal(2, row.PlannedWorkingSets);
        Assert.Equal(0, row.CompletedWorkingSets);
        Assert.Null(row.SkippedSetReason);
        Assert.Null(row.ActualAverageRir);
    }


    [Fact]
    public void UnstructuredRecentSessionBlocksVolumeRecommendationsFromStaleHistory()
    {
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";
        const string fatigue = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1}]}]""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", null, null),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription)
        };

        Assert.Empty(TrainingExerciseVolumeFeedbackMapper.Map(records));
    }

    [Fact]
    public void OlderUnstructuredSessionDoesNotInvalidateNewerStructuredHistory()
    {
        const string prescription = """{"exercises":[{"exerciseId":"bench","sets":3,"targetRepetitionsInReserve":2}]}""";
        const string fatigue = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1}]}]""";
        // SQL supplies newest-first history. The unusable row is older than the three valid exposures.
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", fatigue, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", null, null)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendation.Action);
    }

    private static TrainingExerciseVolumeExposure Exposure(
        string id,
        int planned = 3,
        string? skippedReason = null,
        bool completedSession = true,
        bool pain = false,
        decimal? actualRir = null) =>
        new(id, planned, skippedReason is null ? planned : planned - 1,
            skippedReason, completedSession, pain, TargetRir: 2m, ActualAverageRir: actualRir);
}
