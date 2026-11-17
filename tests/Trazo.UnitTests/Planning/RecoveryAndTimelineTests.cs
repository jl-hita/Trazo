using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class RecoveryAdvisorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly string[] Chest = ["Chest"];
    private static readonly string[] Triceps = ["Triceps"];
    private static readonly string[] Quads = ["Quadriceps"];
    private static readonly string[] None = [];

    private static RecoveryExposure Pressing(double hoursAgo, int sets = 4, decimal? rir = 2m, string recovery = "Unknown", decimal? rpe = null, bool pain = false,
        bool loaded = false) =>
        new(Now.AddHours(-hoursAgo), [new RecoveryExercise(Chest, Triceps, sets, rir, loaded)], recovery, rpe, pain);

    private static RecoveryCandidate Session(int index, string title, string[] primary, string[] secondary, int sets = 4) =>
        new(Guid.NewGuid(), index, title, [new RecoveryPlannedExercise(primary, secondary, sets)]);

    private static RecoveryAssessment Assess(RecoveryCandidate candidate, RecoveryExposure[] history,
        TrainingExperience experience = TrainingExperience.Intermediate) =>
        RecoveryAdvisor.Assess(candidate, history, Now, experience);

    [Fact]
    public void WithoutHistoryEverythingIsReady()
    {
        var assessment = Assess(Session(1, "Empuje", Chest, Triceps), []);
        Assert.Equal(RecoveryState.Ready, assessment.State);
        Assert.Null(assessment.AvailableAt);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void ASessionThatDoesNotOverlapWithWhatWasTrainedIsReadyRightAway()
    {
        var assessment = Assess(Session(2, "Pierna", Quads, None), [Pressing(hoursAgo: 2)]);
        Assert.Equal(RecoveryState.Ready, assessment.State);
    }

    [Fact]
    public void TheSameMusclesTrainedHoursAgoNeedMoreRest_AndTheReasonNamesThem()
    {
        var assessment = Assess(Session(2, "Empuje B", Chest, Triceps), [Pressing(hoursAgo: 10)]);
        Assert.Equal(RecoveryState.Wait, assessment.State);
        Assert.NotNull(assessment.AvailableAt);
        Assert.True(assessment.AvailableAt > Now);
        Assert.Contains(assessment.Reasons, r => r.StartsWith("Pecho:") && r.Contains("10 h"));
    }

    [Fact]
    public void TheMusclesRecoverWithTime_NoFixedRest()
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        Assert.Equal(RecoveryState.Wait, Assess(candidate, [Pressing(hoursAgo: 20)]).State);
        Assert.Equal(RecoveryState.Ready, Assess(candidate, [Pressing(hoursAgo: 96)]).State);
    }

    [Fact]
    public void WhenItIsAlmostRecoveredTheAdviceIsCautionNotWait()
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        // Chest, 4 sets at RIR 2, intermediate: 24 + 5.5 * 4 = 46 h. Eight hours short is Caution.
        var assessment = Assess(candidate, [Pressing(hoursAgo: 38)]);
        Assert.Equal(RecoveryState.Caution, assessment.State);
    }

    [Fact]
    public void MoreSetsCloserToFailureTakeLongerToRecoverFrom()
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        var light = Assess(candidate, [Pressing(hoursAgo: 12, sets: 4, rir: 3m)]);
        var heavy = Assess(candidate, [Pressing(hoursAgo: 12, sets: 10, rir: 0m)]);
        Assert.True(heavy.AvailableAt > light.AvailableAt);
    }

    [Theory]
    [InlineData("Insufficient")]
    [InlineData("Partial")]
    public void ReportedPoorRecoveryHighEffortPainAndPreviousFatigueLengthenTheRest(string recovery)
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        var baseline = Assess(candidate, [Pressing(hoursAgo: 12)]).AvailableAt;
        Assert.True(Assess(candidate, [Pressing(hoursAgo: 12, recovery: recovery)]).AvailableAt > baseline);
        Assert.True(Assess(candidate, [Pressing(hoursAgo: 12, rpe: 9.5m)]).AvailableAt > baseline);
        Assert.True(Assess(candidate, [Pressing(hoursAgo: 12, pain: true)]).AvailableAt > baseline);
        Assert.True(Assess(candidate, [Pressing(hoursAgo: 12, loaded: true)]).AvailableAt > baseline);
        Assert.True(Assess(candidate, [Pressing(hoursAgo: 12, recovery: "Good")]).AvailableAt < baseline);
    }

    [Fact]
    public void TheExperienceLevelScalesTheWindow()
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        var beginner = Assess(candidate, [Pressing(hoursAgo: 12)], TrainingExperience.Beginner).AvailableAt;
        var advanced = Assess(candidate, [Pressing(hoursAgo: 12)], TrainingExperience.Advanced).AvailableAt;
        Assert.True(beginner < advanced);
    }

    [Fact]
    public void AMuscleOnlyWorkedAsASecondaryOrTrivially_DoesNotHoldTheSessionBack()
    {
        // Triceps got 4 secondary sets = 2 effective: the threshold of a real stimulus. A triceps-primary session is not held by it
        // when it was worked with fewer than the minimum effective sets.
        var lightPress = new RecoveryExposure(Now.AddHours(-5), [new RecoveryExercise(Chest, Triceps, 2, 2m, false)], "Unknown", null, false);
        var assessment = Assess(Session(2, "Brazos", Triceps, None), [lightPress]);
        Assert.Equal(RecoveryState.Ready, assessment.State);
    }

    [Fact]
    public void TheNextSessionIsAdvisedAgainstAndALaterOneThatIsReadyIsOffered()
    {
        var chestAgain = Session(2, "Empuje B", Chest, Triceps);
        var legs = Session(3, "Pierna", Quads, None);
        var recommendation = RecoveryAdvisor.Recommend([legs, chestAgain], [Pressing(hoursAgo: 8)], Now, TrainingExperience.Intermediate);
        // Candidates are taken in program order (the chest session is first by index).
        Assert.Equal(chestAgain.SessionId, recommendation.Next.SessionId);
        Assert.Equal(RecoveryState.Wait, recommendation.Next.State);
        Assert.Equal([legs.SessionId], recommendation.Alternatives.Select(a => a.SessionId));
    }

    [Fact]
    public void ARecoveredNextSessionOffersNoAlternatives()
    {
        var recommendation = RecoveryAdvisor.Recommend(
            [Session(2, "Pierna", Quads, None), Session(3, "Empuje", Chest, Triceps)], [Pressing(hoursAgo: 4)], Now, TrainingExperience.Intermediate);
        Assert.Equal(RecoveryState.Ready, recommendation.Next.State);
        Assert.Empty(recommendation.Alternatives);
    }

    [Fact]
    public void TheMostLimitingWorkoutOfAMuscleDecides()
    {
        var candidate = Session(2, "Empuje B", Chest, Triceps);
        var heavyLongAgo = Pressing(hoursAgo: 40, sets: 12, rir: 0m);
        var lightRecent = Pressing(hoursAgo: 3, sets: 2, rir: 3m);
        var both = Assess(candidate, [heavyLongAgo, lightRecent]).AvailableAt;
        Assert.Equal(Assess(candidate, [heavyLongAgo]).AvailableAt, both);
    }
}

public sealed class CalendarTimelineTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    private static TimelineSession S(int index, int period, DateOnly? date) => new(Guid.NewGuid(), index, period, date, $"S{index}");

    [Fact]
    public void TheNextSessionIsTheFirstPendingOneOfTheProgram_EvenIfItsDateHasPassed()
    {
        var pending = new[] { S(5, 2, Today.AddDays(2)), S(3, 1, Today.AddDays(-4)), S(4, 2, Today.AddDays(-1)) };
        var result = new CalendarTimeline().Next(pending, Today, 3);
        Assert.Equal(3, result.Next!.SequenceIndex);
        Assert.Equal([3, 4, 5], result.Candidates.Select(c => c.SequenceIndex));
        Assert.Equal([3, 4], result.Overdue.Select(c => c.SequenceIndex));
    }

    [Fact]
    public void ASessionPlannedForTodayIsNotLate()
    {
        var result = new CalendarTimeline().Next([S(1, 1, Today)], Today, 3);
        Assert.Empty(result.Overdue);
    }

    [Fact]
    public void WithNothingPendingThereIsNoNextSession()
    {
        var result = new CalendarTimeline().Next([], Today, 3);
        Assert.Null(result.Next);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void TheModeIsCalendar() => Assert.Equal("Calendar", new CalendarTimeline().Mode);
}
