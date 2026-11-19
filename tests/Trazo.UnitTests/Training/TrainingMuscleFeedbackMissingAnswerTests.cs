using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

/// <summary>
/// A session without a questionnaire is an unknown, not a signal. These tests run the mapper and the adapter together,
/// the way the plan service does, so a missing answer can never manufacture consecutive low-stimulus evidence.
/// </summary>
public sealed class TrainingMuscleFeedbackMissingAnswerTests
{
    private const string Bench = """{"exercises":[{"exerciseId":"bench","sets":3,"primaryMuscleGroups":["Chest"]}]}""";
    private const string DbBench = """{"exercises":[{"exerciseId":"db-bench","sets":3,"primaryMuscleGroups":["Chest"]}]}""";
    private const string BenchAndFly = """{"exercises":[{"exerciseId":"bench","sets":3,"primaryMuscleGroups":["Chest"]},{"exerciseId":"fly","sets":2,"primaryMuscleGroups":["Chest"]}]}""";
    private static readonly IReadOnlyDictionary<string, int> Planned = new Dictionary<string, int> { ["Chest"] = 10 };
    private static readonly IReadOnlyDictionary<string, int> Ceiling = new Dictionary<string, int> { ["Chest"] = 20 };

    private static string Answer(string exercise, string stimulus, string preFatigue = "recovered", string discomfort = "none") =>
        $$"""[{"exerciseId":"{{exercise}}","stimulus":"{{stimulus}}","discomfort":"{{discomfort}}","preFatigue":"{{preFatigue}}"}]""";

    private static TrainingMuscleFeedbackRecord Session(int day, string? feedback, string prescription = Bench, string outcome = "completed") =>
        new(DateOnly.FromDayNumber(day), outcome, feedback, prescription);

    /// <summary>Newest first, as the plan service supplies it.</summary>
    private static IReadOnlyList<TrainingMuscleFeedbackDecision> Evaluate(params TrainingMuscleFeedbackRecord[] chronological) =>
        TrainingMuscleFeedbackAdapter.Evaluate(
            TrainingMuscleFeedbackMapper.Map(chronological.Reverse()), Planned, Ceiling);

    [Fact]
    public void TwoConsecutiveLowAnswersStillAddOneSet()
    {
        var decisions = Evaluate(Session(1, Answer("bench", "low")), Session(2, Answer("bench", "low")));

        var decision = Assert.Single(decisions);
        Assert.Equal(TrainingMuscleFeedbackAction.AddSet, decision.Action);
        Assert.Equal("bench", decision.ExerciseId);
    }

    [Fact]
    public void TwoLowAnswersSeparatedByAnUnansweredSessionAreNotConsecutive()
    {
        var decisions = Evaluate(Session(1, Answer("bench", "low")), Session(2, null), Session(3, Answer("bench", "low")));

        Assert.DoesNotContain(decisions, d => d.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void SeveralUnansweredSessionsNeverProduceAChange()
    {
        var decisions = Evaluate(Session(1, null), Session(2, null), Session(3, null), Session(4, null));

        Assert.Empty(decisions);
    }

    [Fact]
    public void AnUnansweredSessionIsNeitherNegativeNorPositive()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([Session(2, null)]);

        var exposure = Assert.Single(exposures);
        Assert.Null(exposure.Stimulus);
        Assert.Null(exposure.Discomfort);
        Assert.Null(exposure.PreFatigue);
        Assert.Empty(TrainingMuscleFeedbackAdapter.Evaluate(exposures, Planned, Ceiling));
    }

    [Fact]
    public void ALowAnswerFollowedByAHighOneDoesNotAddASet()
    {
        var decisions = Evaluate(Session(1, Answer("bench", "low")), Session(2, Answer("bench", "high", "loaded")));

        Assert.DoesNotContain(decisions, d => d.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void ASessionWithOnlySomeExercisesAnsweredDoesNotCountAsLowForTheWholeMuscle()
    {
        // Bench answered low in both sessions, but the fly was never answered: the muscle is not "low across the board".
        var decisions = Evaluate(
            Session(1, Answer("bench", "low"), BenchAndFly), Session(2, Answer("bench", "low"), BenchAndFly));

        Assert.DoesNotContain(decisions, d => d.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void AnOmittedSessionIsNotEvidence()
    {
        var decisions = Evaluate(Session(1, Answer("bench", "low")), Session(2, Answer("bench", "low"), outcome: "partial"));

        Assert.DoesNotContain(decisions, d => d.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void ASubstitutedExerciseKeepsTheMuscleEvidenceAndTheChangeGoesToTheNewExercise()
    {
        var decisions = Evaluate(Session(1, Answer("bench", "low")), Session(2, Answer("db-bench", "low"), DbBench));

        var decision = Assert.Single(decisions);
        Assert.Equal(TrainingMuscleFeedbackAction.AddSet, decision.Action);
        Assert.Equal("db-bench", decision.ExerciseId);
    }

    [Fact]
    public void AnUnansweredSessionDoesNotHideStrongDiscomfortOnTheLatestAnswer()
    {
        var decisions = Evaluate(Session(1, null), Session(2, Answer("bench", "low", discomfort: "strong")));

        Assert.Contains(decisions, d => d.Action == TrainingMuscleFeedbackAction.PauseExercise && d.ExerciseId == "bench");
    }
}
