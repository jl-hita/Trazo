using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Workouts as they are really logged: a back-off set, a set left out for lack of time, the reserve not written down, another
/// exercise left unfinished. Progression has to keep working, without reading more than the data says.
/// </summary>
public sealed class RealWorkoutProgressionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static SetRecord Done(decimal kg = 60, int reps = 10, decimal? rir = 2) => new(kg, reps, rir, true, null, false);

    private static SetRecord Skipped(string? reason, decimal kg = 60) => new(kg, null, null, false, reason, false);

    private static ExerciseAdvice Advise(SetRecord[] sets, int times = 2, WorkoutOutcome outcome = WorkoutOutcome.Completed)
    {
        var history = Enumerable.Range(0, times).Select(i => new ExerciseExposureRecord(
            T0.AddDays(3 * i), "bench-press", 3, 6, 10, 2m, TrainingProgressionRule.DoubleProgression, true, sets, outcome, "Good")).ToArray();
        var sessions = history.Select(e => new SessionRecord(e.At, outcome, "None", "Good", false, 7, 1m)).ToArray();
        return AdaptationPlanner.Evaluate(new AdaptationInput(history, sessions, ["bench-press"], [], history[^1].At)).Exercises.Single();
    }

    private static void Raises(ExerciseAdvice advice, decimal to = 62.5m)
    {
        Assert.Equal(nameof(TrainingExerciseProgressionAction.IncreaseLoad), advice.Action);
        Assert.Equal(to, advice.NextLoadKg);
    }

    [Fact]
    public void AHeavyDayAndAVolumeDayProgressEachOnItsOwn()
    {
        // The same exercise twice a week: 3-6 repetitions one day, 6-10 the other, alternating.
        var history = Enumerable.Range(0, 4).Select(i =>
        {
            var heavy = i % 2 == 1;
            var sets = heavy ? new[] { Done(70, 6), Done(70, 6), Done(70, 6) } : [Done(), Done(), Done()];
            return new ExerciseExposureRecord(T0.AddDays(2 * i), "bench-press", 3, heavy ? 3 : 6, heavy ? 6 : 10, 2m,
                TrainingProgressionRule.DoubleProgression, true, sets, WorkoutOutcome.Completed, "Good");
        }).ToArray();
        var sessions = history.Select(e => new SessionRecord(e.At, WorkoutOutcome.Completed, "None", "Good", false, 7, 1m)).ToArray();

        var advice = AdaptationPlanner.Evaluate(new AdaptationInput(history, sessions, ["bench-press"], [], history[^1].At)).Exercises.Single();

        Raises(advice, 72.5m);
    }

    [Fact]
    public void PainOnTheOtherRange_StillHoldsTheLoad()
    {
        var painful = new ExerciseExposureRecord(T0.AddDays(2), "bench-press", 3, 3, 6, 2m, TrainingProgressionRule.DoubleProgression, true,
            [Done(70, 5) with { Pain = true }], WorkoutOutcome.Completed, "Good");
        ExerciseExposureRecord Volume(int day) => new(T0.AddDays(day), "bench-press", 3, 6, 10, 2m, TrainingProgressionRule.DoubleProgression, true,
            [Done(), Done(), Done()], WorkoutOutcome.Completed, "Good");
        ExerciseExposureRecord[] history = [Volume(0), painful, Volume(4)];

        var advice = AdaptationPlanner.Evaluate(new AdaptationInput(history, [], ["bench-press"], [], T0.AddDays(4))).Exercises.Single();

        Assert.Equal(nameof(TrainingExerciseProgressionAction.HoldAndReview), advice.Action);
    }

    [Fact]
    public void OneSetAtAnotherLoad_IsLeftAside()
    {
        Raises(Advise([Done(), Done(), Done(55)]));
        Raises(Advise([Done(65, 6), Done(), Done()]));
    }

    [Fact]
    public void TheNextLoadIsTheOneOfMostSets_NotOfOneHeavyAttempt()
    {
        var advice = Advise([Done(65, 7), Done(60, 8), Done(60, 8)], times: 1);

        Assert.Equal(nameof(TrainingExerciseProgressionAction.AddRepetitions), advice.Action);
        Assert.Equal(60m, advice.NextLoadKg);
        Assert.Equal([9, 9, 9], advice.SuggestedRepetitions);
    }

    [Fact]
    public void ALighterSetThatMissedTheRange_StopsTheIncrease()
    {
        var advice = Advise([Done(), Done(), Done(50, 4)]);

        Assert.NotEqual(nameof(TrainingExerciseProgressionAction.IncreaseLoad), advice.Action);
    }

    [Fact]
    public void EverySetAtADifferentLoad_GivesNoProgression()
    {
        var advice = Advise([Done(50), Done(55), Done(60)]);

        Assert.Equal(nameof(TrainingExerciseProgressionAction.InvalidData), advice.Action);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("equipment")]
    [InlineData("motivation")]
    public void OneSetLeftOutForAnOutsideReason_StillCounts(string reason) =>
        Raises(Advise([Done(), Done(), Skipped(reason)], outcome: WorkoutOutcome.Partial));

    [Theory]
    [InlineData("fatigue")]
    [InlineData("load_too_heavy")]
    [InlineData("illness")]
    [InlineData(null)]
    public void ASetLeftOutForAnotherReason_DoesNotRaiseTheLoad(string? reason)
    {
        var advice = Advise([Done(), Done(), Skipped(reason)], outcome: WorkoutOutcome.Partial);

        Assert.NotEqual(nameof(TrainingExerciseProgressionAction.IncreaseLoad), advice.Action);
    }

    [Fact]
    public void TwoSetsLeftOut_AreNotEnoughEvidence()
    {
        var advice = Advise([Done(), Skipped("time"), Skipped("time")], outcome: WorkoutOutcome.Partial);

        Assert.NotEqual(nameof(TrainingExerciseProgressionAction.IncreaseLoad), advice.Action);
    }

    [Fact]
    public void AnUnfinishedSessionDoesNotHoldBackAnExerciseThatWasFinished()
    {
        // The session is partial because of ANOTHER exercise; the completion of the session itself stays high.
        Raises(Advise([Done(), Done(), Done()], outcome: WorkoutOutcome.Partial));
    }

    [Fact]
    public void WithoutTheReserve_ThreeSessionsAreNeeded_AndItIsExplained()
    {
        SetRecord[] sets = [Done(rir: null), Done(rir: null), Done(rir: null)];

        var twice = Advise(sets, times: 2);
        Assert.Equal(nameof(TrainingExerciseProgressionAction.ConfirmTopOfRange), twice.Action);
        Assert.Contains("RIR", twice.Explanation);
        Assert.DoesNotContain("superó", twice.Explanation);

        var thrice = Advise(sets, times: 3);
        Raises(thrice);
        Assert.Contains("Tres exposiciones", thrice.Explanation);
    }

    [Fact]
    public void AReserveThatWasWrittenAndFellShort_StillHolds()
    {
        var advice = Advise([Done(), Done(rir: null), Done(rir: 0)], times: 3);

        Assert.Equal(nameof(TrainingExerciseProgressionAction.HoldAndReview), advice.Action);
    }
}
