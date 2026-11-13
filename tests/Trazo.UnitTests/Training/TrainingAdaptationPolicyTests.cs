using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingAdaptationPolicyTests
{
    [Fact]
    public void EmptyHistory_DoesNotInventARecommendation()
    {
        var result = TrainingAdaptationPolicy.Evaluate(Array.Empty<TrainingSessionFeedback>());
        Assert.Equal(TrainingAdaptationAction.InsufficientData, result.Action);
    }

    [Fact]
    public void MissedForTime_ReorganizesOnlyFutureSessions()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Time)
        ]);
        Assert.Equal(TrainingAdaptationAction.ReorganizeFutureSessions, result.Action);
    }

    [Fact]
    public void PartialSessionForTime_ReorganizesFutureSessionsWhenCompletionIsAdequate()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Time,
                Recovery: TrainingRecoveryRating.Good, SetCompletionRatio: 0.9m)
        ]);
        Assert.Equal(TrainingAdaptationAction.ReorganizeFutureSessions, result.Action);
    }

    [Fact]
    public void PartialSessionForTimeWithLowSetCompletion_HoldsProgressionFirst()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Time,
                Recovery: TrainingRecoveryRating.Good, SetCompletionRatio: 0.5m)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void PartialSessionForTimeWithInsufficientRecovery_HoldsBeforeReorganization()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Time,
                Recovery: TrainingRecoveryRating.Insufficient, SetCompletionRatio: 0.9m)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void FatigueRelatedPartialSession_HoldsProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Fatigue, TrainingRecoveryRating.Partial)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void IllnessReasonOnCompletedSession_DoesNotTriggerNonCompletionRule()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, TrainingNonCompletionReason.Illness,
                Recovery: TrainingRecoveryRating.Good, SessionRpe: 6)
        ]);
        Assert.Equal(TrainingAdaptationAction.NoChange, result.Action);
    }

    [Fact]
    public void FatigueReasonOnCompletedSession_DoesNotTriggerNonCompletionRule()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, TrainingNonCompletionReason.Fatigue,
                Recovery: TrainingRecoveryRating.Good, SessionRpe: 6)
        ]);
        Assert.Equal(TrainingAdaptationAction.NoChange, result.Action);
    }

    [Fact]
    public void RepeatedInsufficientRecovery_ReviewsTrainingStress()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient)
        ]);
        Assert.Equal(TrainingAdaptationAction.ReviewAndReduceTrainingStress, result.Action);
    }

    [Fact]
    public void NewOrWorseningPain_TakesPriorityOverProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
                PainSignal: TrainingPainSignal.NewOrWorsening, SessionRpe: 6, ProgressionTargetAchieved: true)
        ]);
        Assert.Equal(TrainingAdaptationAction.PauseAffectedExerciseAndReview, result.Action);
    }

    [Fact]
    public void SessionAggregateFeedbackCannotDecideExerciseProgression()
    {
        var feedback = new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
            SessionRpe: 6, ProgressionTargetAchieved: true);
        Assert.Equal(TrainingAdaptationAction.NoChange, TrainingAdaptationPolicy.Evaluate([feedback]).Action);
    }

    [Theory]
    [InlineData(null, TrainingRecoveryRating.Good)]
    [InlineData(true, TrainingRecoveryRating.Unknown)]
    [InlineData(true, TrainingRecoveryRating.Partial)]
    [InlineData(false, TrainingRecoveryRating.Good)]
    public void MissingOrIncompleteProgressionEvidence_DoesNotProgress(bool? progressionTargetAchieved, TrainingRecoveryRating recovery)
    {
        var feedback = new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: recovery,
            SessionRpe: 7, ProgressionTargetAchieved: progressionTargetAchieved);
        Assert.NotEqual(TrainingAdaptationAction.ProgressAccordingToPrescription, TrainingAdaptationPolicy.Evaluate([feedback]).Action);
    }

    [Fact]
    public void LowSetCompletionRatio_HoldsProgressionEvenWhenSessionWasOtherwiseCompleted()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed,
                Recovery: TrainingRecoveryRating.Good, SessionRpe: 6,
                ProgressionTargetAchieved: true, SetCompletionRatio: 0.5m)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
        Assert.Contains("80 %", result.Explanation);
    }

    [Fact]
    public void CompleteSetVolumeDoesNotOverridePainSafetyBlock()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed,
                Recovery: TrainingRecoveryRating.Good, PainSignal: TrainingPainSignal.NewOrWorsening,
                SessionRpe: 6, ProgressionTargetAchieved: true, SetCompletionRatio: 1m)
        ]);
        Assert.Equal(TrainingAdaptationAction.PauseAffectedExerciseAndReview, result.Action);
    }

    [Fact]
    public void VeryHighEffort_HoldsProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
                SessionRpe: 9, ProgressionTargetAchieved: true)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void UnknownRecovery_IsNotTreatedAsGoodRecovery()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, SessionRpe: 6, ProgressionTargetAchieved: true)
        ]);
        Assert.NotEqual(TrainingAdaptationAction.ProgressAccordingToPrescription, result.Action);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    public void OutOfRangeRpe_IsRejected(decimal rpe)
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, SessionRpe: rpe)
        ]);
        Assert.Equal(TrainingAdaptationAction.InvalidFeedback, result.Action);
    }

    [Fact]
    public void UndefinedEnumValue_IsRejected()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback((TrainingSessionOutcome)999)
        ]);
        Assert.Equal(TrainingAdaptationAction.InvalidFeedback, result.Action);
    }

    [Fact]
    public void NullHistory_ThrowsInsteadOfBeingSilentlyAccepted()
    {
        Assert.Throws<ArgumentNullException>(() => TrainingAdaptationPolicy.Evaluate(null!));
    }
}
