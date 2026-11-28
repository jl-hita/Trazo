using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>A load far below the person's moves in one session, within ten percent, and goes back at the first failure.</summary>
public sealed class LoadCalibrationTests
{
    private static readonly TrainingExerciseProgressionPrescription Prescription =
        new("bench-press", 3, 8, 12, 2m, TrainingProgressionRule.DoubleProgression, SmallestLoadIncrementKg: 2.5m);

    private static TrainingExerciseExposure Exposure(decimal load, int repetitions, decimal? rir, bool recovered = true) =>
        new(Enumerable.Range(0, 3).Select(_ => new TrainingExecutedSet("working", load, repetitions, rir, true)).ToArray(),
            RecoveryInsufficient: !recovered);

    [Fact]
    public void OneSessionWithPlentyToSpareRaisesTheLoadByNoMoreThanTenPercent()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, 12, 5)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(65m, result.SuggestedLoadKg); // 66 would be ten percent: the step under it
    }

    [Theory]
    [InlineData(12, 3)] // one repetition more than asked is not a surplus
    [InlineData(11, 6)] // not at the top of the range
    [InlineData(12, null)] // the reserve was not written down
    public void WithoutAClearSurplusTheUsualConfirmationApplies(int repetitions, int? rir)
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, repetitions, rir)]);

        Assert.NotEqual(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
    }

    [Fact]
    public void ACeilingWrittenForTheExerciseIsKept()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(
            Prescription with { MaximumLoadIncreasePercent = 3m }, [Exposure(100, 12, 5)]);

        Assert.Equal(102.5m, result.SuggestedLoadKg);
    }

    [Fact]
    public void PoorRecoveryAndPainStopIt()
    {
        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview,
            TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, 12, 5, recovered: false)]).Action);
        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview,
            TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, 12, 5) with { PainReported = true }]).Action);
    }

    [Fact]
    public void OneFailureAfterTheJumpTakesItBack()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, 12, 5), Exposure(65, 6, 0)]);

        Assert.Equal(TrainingExerciseProgressionAction.ReduceLoad, result.Action);
        Assert.Equal(60m, result.SuggestedLoadKg);
    }

    [Fact]
    public void AnOrdinaryStepIsNotTakenBackAtOnce()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Exposure(60, 12, 2), Exposure(62.5m, 6, 0)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }
}
