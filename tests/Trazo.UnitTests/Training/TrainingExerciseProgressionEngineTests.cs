using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingExerciseProgressionEngineTests
{
    private static TrainingExerciseProgressionPrescription Prescription(decimal? increment = 2.5m) =>
        new("bench-press", 3, 8, 12, 2m, TrainingProgressionRule.DoubleProgression, increment);

    private static TrainingExerciseExposure Exposure(
        int reps, decimal rir = 2m, decimal load = 60m, bool recovered = true) =>
        new(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("working", load, reps, rir, true)).ToArray(),
            RecoveryInsufficient: !recovered);

    [Fact]
    public void BelowTopOfRange_AddsAtMostOneRepPerSetAndKeepsLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [Exposure(10)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
        Assert.Equal(60m, result.SuggestedLoadKg);
        Assert.Equal(new[] { 11, 11, 11 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void ReachingTopOnce_RequiresConfirmationBeforeLoadIncrease()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void TwoConsecutiveTopRangeExposures_IncreaseBySmallestConfiguredIncrement()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(1.25m),
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(61.25m, result.SuggestedLoadKg);
        Assert.Equal(1.25m, result.LoadIncreaseKg);
        Assert.Equal(new[] { 8, 8, 8 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void LoadIncreaseWithoutKnownEquipmentIncrement_IsBoundedAndNotInvented()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(null),
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Null(result.SuggestedLoadKg);
        Assert.Null(result.LoadIncreaseKg);
        Assert.Equal(5m, result.MaximumLoadIncreasePercent);
        Assert.Contains("menor incremento disponible", result.Explanation);
    }

    [Fact]
    public void AnIncrementAboveAnExplicitCeiling_IsRejected()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(5m) with { MaximumLoadIncreasePercent = 5m },
            [Exposure(12, load: 60m), Exposure(12, load: 60m)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Theory]
    [InlineData(12, 1, 12)]   // 13 kg is expected to allow 8 repetitions: the top of the range is enough
    [InlineData(40, 2.5, 12)]
    [InlineData(6, 1, 15)]    // 7 kg is a big step from 6: three repetitions above the range are needed first
    [InlineData(20, 2.5, 13)]
    public void AStepAboveTheDefaultCeiling_IsEarnedWithRepetitions(double load, double increment, int required)
    {
        var prescription = Prescription((decimal)increment);
        TrainingExerciseExposure At(int reps) => Exposure(reps, load: (decimal)load);

        var ready = TrainingExerciseProgressionEngine.Evaluate(prescription, [At(required), At(required)]);
        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, ready.Action);
        Assert.Equal((decimal)(load + increment), ready.SuggestedLoadKg);

        if (required == prescription.MaxRepetitions) return;
        var notYet = TrainingExerciseProgressionEngine.Evaluate(prescription, [At(required - 1), At(required - 1)]);
        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, notYet.Action);
        Assert.Equal((decimal)load, notYet.SuggestedLoadKg);
        Assert.Equal(new[] { required, required, required }, notYet.SuggestedRepetitionsBySet);
        Assert.Contains($"llega a {required} repeticiones", notYet.Explanation);
    }

    [Fact]
    public void OnlyOneExposureWithTheRepetitionsOfTheStep_IsNotEnough()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(1m),
            [Exposure(12, load: 6m), Exposure(15, load: 6m)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
        Assert.Equal(6m, result.SuggestedLoadKg);
        Assert.Equal(new[] { 15, 15, 15 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void WeeklyChangesOfSetsAndReserveDoNotBreakTheStreak()
    {
        var introductory = new TrainingExerciseExposure(
            [new("working", 60m, 12, 3m, true), new("working", 60m, 12, 3m, true)],
            HistoricalPrescription: new(2, 8, 12, 3m));
        var current = Exposure(12) with { HistoricalPrescription = new(3, 8, 12, 2m) };

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [introductory, current]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(62.5m, result.SuggestedLoadKg);
    }

    [Fact]
    public void AShorterWeekIsNotCountedAsAFailedOne()
    {
        var shorter = new TrainingExerciseExposure(
            [new("working", 60m, 10, 2m, true), new("working", 60m, 10, 2m, true)],
            HistoricalPrescription: new(2, 8, 12, 2m));
        var failed = Exposure(6) with { HistoricalPrescription = new(3, 8, 12, 2m) };

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [shorter, failed]);

        Assert.NotEqual(TrainingExerciseProgressionAction.ReduceLoad, result.Action);
    }

    [Fact]
    public void ThreeTopOfRangeExposuresStillProgressLoadInsteadOfBeingFlaggedAsStagnant()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(1.25m),
            [Exposure(12), Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(61.25m, result.SuggestedLoadKg);
    }

    [Fact]
    public void ExerciseSpecificIncreaseCeilingOverridesDefault()
    {
        var prescription = Prescription(null) with { MaximumLoadIncreasePercent = 2m };
        var result = TrainingExerciseProgressionEngine.Evaluate(prescription,
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(2m, result.MaximumLoadIncreasePercent);
        Assert.Equal(new[] { 8, 8, 8 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void InvalidIncreaseCeilingRejectsPrescription()
    {
        var prescription = Prescription() with { MaximumLoadIncreasePercent = 11m };
        var result = TrainingExerciseProgressionEngine.Evaluate(prescription, [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.InvalidPrescription, result.Action);
    }

    [Fact]
    public void RirBelowTarget_HoldsProgressionEvenWhenRepsAreAtMaximum()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12, rir: 1m), Exposure(12, rir: 1m)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void MissingRir_WhenPrescriptionHasTarget_AsksForAnotherSession()
    {
        var exposure = new TrainingExerciseExposure(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("working", 60m, 12, null, true)).ToArray());

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure, exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
    }

    [Fact]
    public void IncompleteSet_HoldsProgression()
    {
        var exposure = new TrainingExerciseExposure([
            new("working", 60m, 12, 2m, true),
            new("working", 60m, 12, 2m, true),
            new("working", 60m, 12, 2m, false)
        ]);

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void WarmupSetsAreNotAcceptedAsWorkingSets()
    {
        var exposure = new TrainingExerciseExposure(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("warmup", 20m, 12, 5m, true)).ToArray());

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void RecentPainBlocksProgressionAcrossLaterPainFreeRecord()
    {
        var painful = Exposure(12) with { PainReported = true };
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [painful, Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void InsufficientRecoveryBlocksProgression()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12), Exposure(12, recovered: false)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void IncompleteLatestExposureCannotBeSkippedToProgressFromOlderHistory()
    {
        var incomplete = Exposure(12) with { SessionCompleted = false };
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12), Exposure(12), incomplete]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void DifferentLoadsAcrossWorkingSets_AreNotAveraged()
    {
        var exposure = new TrainingExerciseExposure([
            new("working", 60m, 12, 2m, true),
            new("working", 57.5m, 12, 2m, true),
            new("working", 55m, 12, 2m, true)
        ]);

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.InvalidData, result.Action);
    }

    [Fact]
    public void RepetitionsAboveTheMaximumCountAsReachingTheTop()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(13), Exposure(13)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(62.5m, result.SuggestedLoadKg);
    }

    [Fact]
    public void LowRepetitions_HoldRatherThanAutomaticallyReducingOrIncreasingLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(6)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void ThreeExposuresWithoutRepetitionOrEffortImprovement_FlagStagnation()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(10), Exposure(10), Exposure(10)]);

        Assert.Equal(TrainingExerciseProgressionAction.StagnationReview, result.Action);
        Assert.Contains("tres exposiciones", result.Explanation);
    }

    [Fact]
    public void ImprovedRirPreventsFalseStagnation()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(10, rir: 2m), Exposure(10, rir: 2.5m), Exposure(10, rir: 3m)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
    }

    [Fact]
    public void LatestDeloadExposureRequiresAProgressionEligibleSessionBeforeLoadIncrease()
    {
        var deload = Exposure(12) with { ProgressionEligible = false };
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [Exposure(12), deload]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void DeloadExposureBreaksTheConsecutiveTopRangeStreak()
    {
        var deload = Exposure(12) with { ProgressionEligible = false };
        var result = TrainingExerciseProgressionEngine.Evaluate(
            Prescription(), [Exposure(12), deload, Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
    }

    [Fact]
    public void ChangedHistoricalRepRangeBreaksTopOfRangeStreak()
    {
        var currentPrescription = Prescription(1.25m) with { MinRepetitions = 6, MaxRepetitions = 8 };
        var previousBlock = Exposure(12) with
        {
            HistoricalPrescription = new(3, 8, 12, 2m)
        };
        var currentBlock = Exposure(8) with
        {
            HistoricalPrescription = new(3, 6, 8, 2m)
        };

        var result = TrainingExerciseProgressionEngine.Evaluate(
            currentPrescription, [previousBlock, currentBlock]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void LatestExposureWithDifferentHistoricalPrescriptionRequiresFreshEvidence()
    {
        var currentPrescription = Prescription() with { MinRepetitions = 6, MaxRepetitions = 8 };
        var oldRangeExposure = Exposure(12) with
        {
            HistoricalPrescription = new(3, 8, 12, 2m)
        };

        var result = TrainingExerciseProgressionEngine.Evaluate(
            currentPrescription, [oldRangeExposure, oldRangeExposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
        Assert.Contains("prescripción de este ejercicio cambió", result.Explanation);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void ChangedHistoricalSetCountBreaksTopOfRangeStreak()
    {
        var previousBlock = Exposure(12) with
        {
            HistoricalPrescription = new(4, 8, 12, 2m)
        };
        var currentBlock = Exposure(12) with
        {
            HistoricalPrescription = new(3, 8, 12, 2m)
        };

        var result = TrainingExerciseProgressionEngine.Evaluate(
            Prescription(1.25m), [previousBlock, currentBlock]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void DeloadExposureDoesNotCountTowardStagnationDetection()
    {
        var deload = Exposure(10) with { ProgressionEligible = false };
        var result = TrainingExerciseProgressionEngine.Evaluate(
            Prescription(), [Exposure(10), deload, Exposure(10)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
    }

    [Fact]
    public void NoProgressionRule_ReturnsNotApplicable()
    {
        var p = Prescription() with { ProgressionRule = TrainingProgressionRule.None };
        var result = TrainingExerciseProgressionEngine.Evaluate(p, [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.NotApplicable, result.Action);
    }

    [Fact]
    public void RecentPainStillRequiresReviewWhenProgressionIsDisabled()
    {
        var p = Prescription() with { ProgressionRule = TrainingProgressionRule.None };
        var exposure = new TrainingExerciseExposure(
            Enumerable.Range(0, 3)
                .Select(_ => new TrainingExecutedSet("working", 60m, 12, 3m, true)).ToArray(),
            PainReported: true);

        var result = TrainingExerciseProgressionEngine.Evaluate(p, [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void UnsupportedProgressionRule_IsNotInterpretedAsDoubleProgression()
    {
        var p = Prescription() with { ProgressionRule = TrainingProgressionRule.IncreaseLoadWithinLimits };
        var result = TrainingExerciseProgressionEngine.Evaluate(p, [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.NotApplicable, result.Action);
    }

    [Fact]
    public void InsufficientHistory_DoesNotInventProgression()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), []);

        Assert.Equal(TrainingExerciseProgressionAction.InsufficientData, result.Action);
    }

    [Theory]
    [InlineData(TrainingExerciseProgressionAction.IncreaseLoad)]
    [InlineData(TrainingExerciseProgressionAction.AddRepetitions)]
    [InlineData(TrainingExerciseProgressionAction.ConfirmTopOfRange)]
    public void ActiveHold_BlocksEveryProgressiveRecommendation(TrainingExerciseProgressionAction action)
    {
        var original = new TrainingExerciseProgressionRecommendation(action, "x", 62.5m, 2.5m, [11, 11, 11], 5m);

        var held = TrainingProgressionHold.Apply(original, holdActive: true);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, held.Action);
        Assert.Null(held.SuggestedLoadKg);
        Assert.Null(held.LoadIncreaseKg);
        Assert.Same(original, TrainingProgressionHold.Apply(original, holdActive: false));
    }

    [Fact]
    public void ActiveHold_DoesNotBlockLoadReduction()
    {
        var reduce = new TrainingExerciseProgressionRecommendation(TrainingExerciseProgressionAction.ReduceLoad, "x", 55m, null, null, null);

        Assert.Same(reduce, TrainingProgressionHold.Apply(reduce, holdActive: true));
    }
}
