using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

/// <summary>
/// 28 deterministic scenarios covering the interaction of completion, recovery, pain,
/// perceived effort, progression evidence and recent history. These are software safety
/// regressions, not clinical validation.
/// </summary>
public sealed class TrainingAdaptationPolicyMatrixTests
{
    public static IEnumerable<object[]> Cases =>
    [
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 6, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 8, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 6, ProgressionTargetAchieved: false) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 9, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Partial, SessionRpe: 6, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Unknown, SessionRpe: 6, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient, SessionRpe: 6, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: null, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Fatigue, TrainingRecoveryRating.Partial) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Illness, TrainingRecoveryRating.Partial) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Time) }, TrainingAdaptationAction.ReorganizeFutureSessions],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Time, TrainingRecoveryRating.Insufficient) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Equipment) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Time) }, TrainingAdaptationAction.ReorganizeFutureSessions],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Fatigue) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Illness) }, TrainingAdaptationAction.HoldProgressionAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Equipment) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Motivation) }, TrainingAdaptationAction.NoChange],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Other) }, TrainingAdaptationAction.NoChange],
        [new[] {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient)
        }, TrainingAdaptationAction.ReviewAndReduceTrainingStress],
        [new[] {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 6, ProgressionTargetAchieved: true)
        }, TrainingAdaptationAction.NoChange],
        [new[] {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient),
            new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Time, TrainingRecoveryRating.Insufficient)
        }, TrainingAdaptationAction.ReviewAndReduceTrainingStress],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, PainSignal: TrainingPainSignal.NewOrWorsening, SessionRpe: 6, ProgressionTargetAchieved: true) }, TrainingAdaptationAction.PauseAffectedExerciseAndReview],
        [new[] {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Partial, PainSignal: TrainingPainSignal.NewOrWorsening),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, SessionRpe: 6, ProgressionTargetAchieved: true)
        }, TrainingAdaptationAction.PauseAffectedExerciseAndReview],
        [new[] { new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good, PainSignal: TrainingPainSignal.NewOrWorsening, SessionRpe: 10) }, TrainingAdaptationAction.PauseAffectedExerciseAndReview],
        [new[] {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient, PainSignal: TrainingPainSignal.NewOrWorsening),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient)
        }, TrainingAdaptationAction.PauseAffectedExerciseAndReview],
        [Array.Empty<TrainingSessionFeedback>(), TrainingAdaptationAction.InsufficientData],
        [new[] { new TrainingSessionFeedback((TrainingSessionOutcome)999) }, TrainingAdaptationAction.InvalidFeedback]
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void FeedbackScenariosReturnConservativeDecision(
        TrainingSessionFeedback[] feedback,
        TrainingAdaptationAction expected)
    {
        var decision = TrainingAdaptationPolicy.Evaluate(feedback);

        Assert.Equal(expected, decision.Action);
        Assert.False(string.IsNullOrWhiteSpace(decision.Explanation));
    }

    [Fact]
    public void RecommendationNeverMutatesTheInputHistory()
    {
        var history = new[]
        {
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
                SessionRpe: 6, ProgressionTargetAchieved: true)
        };
        var original = history[0];

        _ = TrainingAdaptationPolicy.Evaluate(history);

        Assert.Same(original, history[0]);
        Assert.Equal(TrainingSessionOutcome.Completed, history[0].Outcome);
        Assert.Equal(TrainingRecoveryRating.Good, history[0].Recovery);
    }
}
