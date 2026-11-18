using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The decision about sessions not done: the queue is kept unless the real state says otherwise.</summary>
public sealed class AdjustmentAdvisorTests
{
    private static AdjustmentAdvice Advise(bool sequence = false, int? days = 2, int overdue = 0, int behind = 0, int resolved = 0, int skipped = 0) =>
        AdjustmentAdvisor.Advise(new AdjustmentInput(sequence, days, overdue, behind, resolved, skipped));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnTrackNothingChanges(bool sequence)
    {
        var advice = Advise(sequence);
        Assert.Equal(AdjustmentStrategy.Continue, advice.Strategy);
        Assert.Equal(AdjustmentReason.OnTrack, advice.Reason);
        Assert.Equal(0, advice.EaseBackSessions);
        Assert.False(advice.CanReschedule);
        Assert.False(advice.SuggestSkip);
    }

    [Fact]
    public void AFirstWorkoutNotYetDoneIsNotAnAbsence() =>
        Assert.Equal(AdjustmentStrategy.Continue, Advise(days: null).Strategy);

    [Fact]
    public void ASequencePlanNeverShiftsAndANeverLateQueueNeverSuggestsSkipping()
    {
        var advice = Advise(sequence: true, days: 10, overdue: 5, behind: 3);
        Assert.Equal(AdjustmentStrategy.Continue, advice.Strategy);
        Assert.False(advice.CanReschedule);
        Assert.False(advice.SuggestSkip);
    }

    [Fact]
    public void ACalendarPlanAWeekBehindOffersToMoveTheProgram()
    {
        var advice = Advise(days: 9, overdue: 3, behind: 1);
        Assert.Equal(AdjustmentStrategy.Shift, advice.Strategy);
        Assert.Equal(AdjustmentReason.BehindCalendar, advice.Reason);
        Assert.True(advice.CanReschedule);
        Assert.False(advice.SuggestSkip); // moving keeps every session: omitting is not suggested
    }

    [Fact]
    public void LateSessionsInTheSameWeekStayPendingAndOnlyALongQueueSuggestsSkipping()
    {
        var one = Advise(overdue: 1);
        Assert.Equal(AdjustmentReason.LateSessions, one.Reason);
        Assert.False(one.SuggestSkip);
        Assert.True(Advise(overdue: AdjustmentAdvisor.SuggestSkipAtOverdue).SuggestSkip);
    }

    [Theory]
    [InlineData(20, 0, AdjustmentStrategy.Continue)]
    [InlineData(21, 1, AdjustmentStrategy.EaseBack)]
    [InlineData(41, 1, AdjustmentStrategy.EaseBack)]
    [InlineData(42, 2, AdjustmentStrategy.EaseBack)]
    [InlineData(83, 2, AdjustmentStrategy.EaseBack)]
    [InlineData(84, 2, AdjustmentStrategy.Rebuild)]
    [InlineData(400, 2, AdjustmentStrategy.Rebuild)]
    public void TheLongerTheAbsenceTheMoreTheReturnIsEased(int days, int easedSessions, AdjustmentStrategy strategy)
    {
        var advice = Advise(days: days);
        Assert.Equal(easedSessions, advice.EaseBackSessions);
        Assert.Equal(strategy, advice.Strategy);
        Assert.Equal(easedSessions, AdjustmentAdvisor.EaseBackSessionsFor(days));
    }

    [Fact]
    public void RepeatedOmissionsSuggestRebuildingButNeverAnOneOff()
    {
        Assert.Equal(AdjustmentStrategy.Continue, Advise(resolved: 5, skipped: 2).Strategy);
        var advice = Advise(resolved: 5, skipped: 3);
        Assert.Equal(AdjustmentStrategy.Rebuild, advice.Strategy);
        Assert.Equal(AdjustmentReason.RepeatedSkips, advice.Reason);
        // Three omitted out of only three resolved is still a pattern.
        Assert.Equal(AdjustmentStrategy.Rebuild, Advise(resolved: 3, skipped: 3).Strategy);
    }

    [Fact]
    public void RebuildKeepsTheEaseBackForWhoeverKeepsThePlan()
    {
        var advice = Advise(days: 30, resolved: 5, skipped: 4);
        Assert.Equal(AdjustmentStrategy.Rebuild, advice.Strategy);
        Assert.Equal(1, advice.EaseBackSessions);
    }

    [Fact]
    public void ADecisionIsDeterministic() =>
        Assert.Equal(Advise(days: 50, overdue: 2, behind: 2, resolved: 4, skipped: 1), Advise(days: 50, overdue: 2, behind: 2, resolved: 4, skipped: 1));
}
