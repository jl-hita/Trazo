using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

/// <summary>
/// Scenario matrix for the automatic per-exercise volume adapter. These cases verify that
/// repeated exercise-specific fatigue is distinguished from pain, incomplete sessions,
/// time/equipment/adherence constraints and insufficient evidence.
/// </summary>
public sealed class TrainingExerciseVolumeAdapterMatrixTests
{
    public static IEnumerable<object[]> Scenarios
    {
        get
        {
            yield return new object[]
            {
                new[] { Exposure(), Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.ProposeReduceOneSet, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue"), Exposure() },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(), Exposure() },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(actualRir: 1m), Exposure(actualRir: 1m) },
                TrainingExerciseVolumeAction.ProposeReduceOneSet, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(actualRir: 1m), Exposure() },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(actualRir: 1m), Exposure(actualRir: 1m) },
                TrainingExerciseVolumeAction.ProposeReduceOneSet, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(actualRir: 1.01m), Exposure(actualRir: 1.01m) },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue", actualRir: 0m), Exposure(), Exposure() },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.NoChange, 2
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue"), Exposure(), Exposure() },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.ProposeReduceOneSet, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "time"), Exposure(skippedReason: "time"), Exposure(skippedReason: "time") },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "equipment"), Exposure(skippedReason: "equipment"), Exposure(skippedReason: "equipment") },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "illness"), Exposure(skippedReason: "illness"), Exposure(skippedReason: "illness") },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "motivation"), Exposure(skippedReason: "motivation"), Exposure(skippedReason: "motivation") },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue", completedSession: false), Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.ProposeReduceOneSet, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue", completedSession: false) },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(planned: 1, skippedReason: "fatigue"), Exposure(planned: 1, skippedReason: "fatigue"), Exposure(planned: 1, skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.ReviewWithoutReduction, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(skippedReason: "fatigue", pain: true), Exposure(skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.PauseAndReview, 3
            };
            yield return new object[]
            {
                new[] { Exposure(skippedReason: "fatigue"), Exposure(skippedReason: "fatigue"), Exposure(pain: true) },
                TrainingExerciseVolumeAction.PauseAndReview, 3
            };
            yield return new object[]
            {
                new[] { Exposure(), Exposure(actualRir: null), Exposure(actualRir: null) },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(targetRir: null, actualRir: 0m), Exposure(targetRir: null, actualRir: 0m), Exposure(targetRir: null, actualRir: 0m) },
                TrainingExerciseVolumeAction.NoChange, 3
            };
            yield return new object[]
            {
                new[] { Exposure(completed: 3, skippedReason: "fatigue"), Exposure(completed: 3, skippedReason: "fatigue"), Exposure(completed: 3, skippedReason: "fatigue") },
                TrainingExerciseVolumeAction.NoChange, 3
            };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ThreeExposureDecisionMatrixIsConservative(
        TrainingExerciseVolumeExposure[] history,
        TrainingExerciseVolumeAction expectedAction,
        int expectedExposureCount)
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(history));

        Assert.Equal(expectedAction, recommendation.Action);
        Assert.Equal(expectedExposureCount, recommendation.ExposuresAnalyzed);
        Assert.Equal(expectedAction == TrainingExerciseVolumeAction.ProposeReduceOneSet ? 2 : null,
            recommendation.SuggestedWorkingSets);
        Assert.False(string.IsNullOrWhiteSpace(recommendation.Explanation));
    }

    [Fact]
    public void ExercisesAreEvaluatedIndependentlyAndReturnedInStableOrder()
    {
        var recommendations = TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("z-row"),
            Exposure("z-row"),
            Exposure("z-row"),
            Exposure("bench"),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("a-curl"),
            Exposure("a-curl", pain: true),
            Exposure("a-curl")
        ]);

        Assert.Equal(new[] { "a-curl", "bench", "z-row" },
            recommendations.Select(item => item.ExerciseId));
        Assert.Equal(TrainingExerciseVolumeAction.PauseAndReview, recommendations[0].Action);
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendations[1].Action);
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendations[2].Action);
    }

    [Fact]
    public void EmptyHistoryAndBlankExerciseIdsDoNotInventRecommendations()
    {
        Assert.Empty(TrainingExerciseVolumeAdapter.Evaluate([]));
        Assert.Empty(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure(" "), Exposure(" "), Exposure(" ")
        ]));
    }

    private static TrainingExerciseVolumeExposure Exposure(
        string id = "bench",
        int planned = 3,
        int? completed = null,
        string? skippedReason = null,
        bool completedSession = true,
        bool pain = false,
        decimal? targetRir = 2m,
        decimal? actualRir = null) =>
        new(
            id,
            planned,
            completed ?? (skippedReason is null ? planned : planned - 1),
            skippedReason,
            completedSession,
            pain,
            targetRir,
            actualRir);
}
