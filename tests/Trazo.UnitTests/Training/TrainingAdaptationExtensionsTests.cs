using System.Text.Json.Nodes;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingAdaptationExtensionsTests
{
    private static TrainingSessionFeedback Stress(
        TrainingRecoveryRating recovery = TrainingRecoveryRating.Insufficient, decimal? rpe = null, decimal? ratio = null) =>
        new(TrainingSessionOutcome.Completed, Recovery: recovery, SessionRpe: rpe, SetCompletionRatio: ratio);

    [Fact]
    public void ThreeSessionsWithCombinedStressSignalsProposeADeloadNeverAnAutomaticOne()
    {
        var decision = TrainingAdaptationPolicy.Evaluate([
            Stress(rpe: 9), Stress(), Stress(ratio: 0.9m, rpe: 9)]);
        Assert.Equal(TrainingAdaptationAction.ProposeDeload, decision.Action);
        Assert.Contains("no se aplica automáticamente", decision.Explanation);
    }

    [Fact]
    public void AllThreeSessionsMustBeStressedAndTwoKindsOfSignalMustAppear()
    {
        // only low recovery repeated: keeps the milder existing action
        Assert.Equal(TrainingAdaptationAction.ReviewAndReduceTrainingStress,
            TrainingAdaptationPolicy.Evaluate([Stress(), Stress(), Stress()]).Action);
        // one healthy session in the window: no deload proposal
        Assert.NotEqual(TrainingAdaptationAction.ProposeDeload, TrainingAdaptationPolicy.Evaluate([
            Stress(rpe: 9), Stress(TrainingRecoveryRating.Good), Stress(rpe: 9)]).Action);
    }

    [Fact]
    public void PainStillTakesPrecedenceOverADeloadProposal()
    {
        var withPain = new TrainingSessionFeedback(TrainingSessionOutcome.Completed,
            PainSignal: TrainingPainSignal.NewOrWorsening, Recovery: TrainingRecoveryRating.Insufficient, SessionRpe: 9);
        Assert.Equal(TrainingAdaptationAction.PauseAffectedExerciseAndReview,
            TrainingAdaptationPolicy.Evaluate([Stress(rpe: 9), Stress(rpe: 9), withPain]).Action);
    }

    private static TrainingExerciseVolumeExposure Reduced(int planned = 2, int original = 3, int completed = 2,
        string? reason = null, decimal? rir = 2m) =>
        new("squat", planned, completed, reason, true, false, 2m, rir, original);

    [Fact]
    public void ThreeCleanExposuresAtReducedVolumeProposeRestoringOneSet()
    {
        var result = TrainingExerciseVolumeAdapter.Evaluate([Reduced(), Reduced(), Reduced()]).Single();
        Assert.Equal(TrainingExerciseVolumeAction.ProposeRestoreOneSet, result.Action);
        Assert.Equal(3, result.SuggestedWorkingSets);
    }

    [Fact]
    public void FatigueOrMissingSetsBlockRestoringVolume()
    {
        Assert.Equal(TrainingExerciseVolumeAction.NoChange,
            TrainingExerciseVolumeAdapter.Evaluate([Reduced(), Reduced(), Reduced(completed: 1)]).Single().Action);
        Assert.NotEqual(TrainingExerciseVolumeAction.ProposeRestoreOneSet,
            TrainingExerciseVolumeAdapter.Evaluate([Reduced(), Reduced(), Reduced(rir: 0.5m)]).Single().Action);
    }

    [Fact]
    public void ReductionRemembersTheOriginalDoseAndRestoreNeverExceedsIt()
    {
        var json = """{"exercises":[{"exerciseId":"squat","exerciseNameSnapshot":"Sentadilla","order":1,"sets":3,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":2,"restSeconds":120,"progressionRule":1,"catalogVersion":"v"}]}""";
        var ids = new HashSet<string> { "squat" };
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json, ids, out var reduced, out _));
        var node = JsonNode.Parse(reduced)!["exercises"]![0]!;
        Assert.Equal(2, (int)node["sets"]!);
        Assert.Equal(3, (int)node["adaptationOriginalSets"]!);

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(reduced, ids, out var twice, out _));
        Assert.Equal(3, (int)JsonNode.Parse(twice)!["exercises"]![0]!["adaptationOriginalSets"]!);

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(twice, ids, out var restored1, out _, out _));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(restored1, ids, out var restored2, out _, out _));
        Assert.Equal(3, (int)JsonNode.Parse(restored2)!["exercises"]![0]!["sets"]!);
        Assert.Null(JsonNode.Parse(restored2)!["exercises"]![0]!["adaptationOriginalSets"]);
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(restored2, ids, out _, out _, out _));
    }

    [Fact]
    public void RaiseReserveAddsOneRirCappedAtFiveAndLeavesSetsUntouched()
    {
        var json = """{"exercises":[{"exerciseId":"a","exerciseNameSnapshot":"A","order":1,"sets":3,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":2,"restSeconds":90,"progressionRule":1,"catalogVersion":"v"},{"exerciseId":"b","exerciseNameSnapshot":"B","order":2,"sets":2,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":5,"restSeconds":90,"progressionRule":1,"catalogVersion":"v"}]}""";
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var updated, out _));
        var ex = JsonNode.Parse(updated)!["exercises"]!;
        Assert.Equal(3m, (decimal)ex[0]!["targetRepetitionsInReserve"]!);
        Assert.Equal(5m, (decimal)ex[1]!["targetRepetitionsInReserve"]!);
        Assert.Equal(3, (int)ex[0]!["sets"]!);
    }
}
