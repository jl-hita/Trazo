using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingLoadReductionTests
{
    private static readonly TrainingExerciseProgressionPrescription Prescription = new(
        "bench", 3, 8, 12, 2m, TrainingProgressionRule.DoubleProgression, 2.5m);

    private static TrainingExecutedSet Set(decimal kg, int reps, bool completed = true) =>
        new("working", kg, reps, 1m, completed);

    private static TrainingExerciseExposure Exposure(
        TrainingExecutedSet[] sets, string? reason = null, bool recoveryInsufficient = false, bool pain = false) =>
        new(sets, SessionCompleted: false, RecoveryInsufficient: recoveryInsufficient, PainReported: pain,
            SkippedSetReason: reason);

    private static TrainingExerciseExposure Failed(decimal kg = 100m, string? reason = "load_too_heavy") =>
        Exposure([Set(kg, 8), Set(kg, 6), Set(kg, 0, false)], reason);

    [Fact]
    public void TwoFailedExposuresAtTheSameLoadReduceTheLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Failed(), Failed()]);
        Assert.Equal(TrainingExerciseProgressionAction.ReduceLoad, result.Action);
        Assert.True(result.SuggestedLoadKg < 100m);
        Assert.True(result.SuggestedLoadKg >= 90m);
        Assert.Equal(0m, result.SuggestedLoadKg % 2.5m);
    }

    [Fact]
    public void ASingleFailureOnlyHoldsTheLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription, [Failed()]);
        Assert.NotEqual(TrainingExerciseProgressionAction.ReduceLoad, result.Action);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("equipment")]
    [InlineData("illness")]
    [InlineData("motivation")]
    public void ExternalReasonsNeverTriggerALoadReduction(string reason)
    {
        var failed = Exposure([Set(100m, 10), Set(100m, 10)], reason);
        Assert.NotEqual(TrainingExerciseProgressionAction.ReduceLoad,
            TrainingExerciseProgressionEngine.Evaluate(Prescription, [failed, failed]).Action);
    }

    [Fact]
    public void PainOrInsufficientRecoveryTakePrecedenceOverLoadReduction()
    {
        var pain = TrainingExerciseProgressionEngine.Evaluate(Prescription,
            [Failed(), Exposure([Set(100m, 8), Set(100m, 6)], "load_too_heavy", pain: true)]);
        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, pain.Action);

        var recovery = TrainingExerciseProgressionEngine.Evaluate(Prescription,
            [Failed(), Exposure([Set(100m, 8), Set(100m, 6)], "load_too_heavy", recoveryInsufficient: true)]);
        Assert.NotEqual(TrainingExerciseProgressionAction.ReduceLoad, recovery.Action);
    }

    [Fact]
    public void DifferentLoadsBetweenExposuresDoNotTriggerAReduction()
    {
        Assert.NotEqual(TrainingExerciseProgressionAction.ReduceLoad,
            TrainingExerciseProgressionEngine.Evaluate(Prescription, [Failed(100m), Failed(95m)]).Action);
    }

    [Fact]
    public void RepsBelowTheMinimumTriggerEvenWithoutAReasonOrMissingSets()
    {
        var low = Exposure([Set(80m, 5), Set(80m, 5), Set(80m, 5)]);
        Assert.Equal(TrainingExerciseProgressionAction.ReduceLoad,
            TrainingExerciseProgressionEngine.Evaluate(Prescription, [low, low]).Action);
    }
}
