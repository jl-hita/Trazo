using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// A period of a plan is a group of sessions, not a length of time. These tests show that the dose a user really receives depends on
/// the instants of their workouts, and that a metric the evidence cannot support is reported as unknown, never estimated.
/// </summary>
public sealed class LoadMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);

    private static readonly DoseExercise Bench = new("bench-press", ["Chest"], ["Triceps"], 4);
    private static readonly DoseExercise Row = new("barbell-row", ["Lats", "UpperBack"], ["Biceps"], 4);

    private static DoseWorkout Workout(DateTimeOffset at) => new(at, [Bench, Row]);

    [Fact]
    public void ASetCountsOneForEveryMuscleTheEngineSaysItStimulates_AndNeverTwiceInOneExercise()
    {
        var effective = LoadMetrics.Effective([Bench, new DoseExercise("bench-press", ["Chest"], ["Chest", "Triceps"], 2)]);
        Assert.Equal(6m, effective["Chest"]);    // 4 + 2, the secondary entry for the same muscle does not add
        Assert.Equal(6m, effective["Triceps"]);  // a press counts in full for the triceps, as the generator counts it
        Assert.False(effective.ContainsKey("Deltoids")); // not among the secondaries that count for a bench press
    }

    [Fact]
    public void FiveSessionsInSixDaysAndInTenDaysHaveTheSameVolumePerRotation_ButNotTheSameDosePerSevenDays()
    {
        DateTimeOffset[] fast = [Now.AddDays(-6), Now.AddDays(-5), Now.AddDays(-3), Now.AddDays(-2), Now];
        DateTimeOffset[] slow = [Now.AddDays(-10), Now.AddDays(-8), Now.AddDays(-5), Now.AddDays(-3), Now];

        // Per rotation (the five sessions) both users did exactly the same work.
        var rotationFast = LoadMetrics.Dose(fast.Select(Workout));
        var rotationSlow = LoadMetrics.Dose(slow.Select(Workout));
        Assert.Equal(rotationFast, rotationSlow);
        Assert.Equal(20m, rotationFast.Single(d => d.Muscle == "Chest").Sets);

        // Per real seven days they did not: only the sessions inside the window count.
        var windowStart = Now.AddDays(-7);
        var inWeekFast = LoadMetrics.Dose(fast.Where(at => at > windowStart).Select(Workout));
        var inWeekSlow = LoadMetrics.Dose(slow.Where(at => at > windowStart).Select(Workout));
        Assert.Equal(20m, inWeekFast.Single(d => d.Muscle == "Chest").Sets);
        Assert.Equal(12m, inWeekSlow.Single(d => d.Muscle == "Chest").Sets);
        Assert.Equal(5m, inWeekFast.Single(d => d.Muscle == "Chest").Exposures);
        Assert.Equal(3m, inWeekSlow.Single(d => d.Muscle == "Chest").Exposures);

        // The observed pace tells them apart too: sessions per seven days between the first and the last workout.
        Assert.Equal(4.7m, LoadMetrics.ObservedSessionsPerSevenDays(fast, Now));
        Assert.Equal(2.8m, LoadMetrics.ObservedSessionsPerSevenDays(slow, Now));
    }

    [Fact]
    public void WithoutEnoughEvidenceThePaceIsUnknown_NotAGuess()
    {
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays([], Now));
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays([Now.AddDays(-1), Now], Now));                      // two workouts
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays([Now.AddDays(-14), Now.AddDays(-7), Now], Now));    // three workouts
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays([Now.AddDays(-3), Now.AddDays(-2), Now.AddDays(-1), Now], Now)); // a streak of four days
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays([Now.AddDays(-60), Now.AddDays(-50), Now.AddDays(-40)], Now)); // too old
        Assert.Equal(1.0m, LoadMetrics.ObservedSessionsPerSevenDays([Now.AddDays(-21), Now.AddDays(-14), Now.AddDays(-7), Now], Now));
    }

    [Fact]
    public void ThreeDaysInARowGiveAHighLocalDoseButNoPaceToTrustYet()
    {
        DateTimeOffset[] streak = [Now.AddDays(-2), Now.AddDays(-1), Now];
        Assert.Null(LoadMetrics.ObservedSessionsPerSevenDays(streak, Now)); // a streak is not a weekly frequency
        var dose = LoadMetrics.Dose(streak.Select(Workout));
        Assert.Equal(3m, dose.Single(d => d.Muscle == "Chest").Exposures);
    }

    [Fact]
    public void AMuscleOnlyCountsAsExposedWhenTheWorkoutReallyWorkedIt()
    {
        // One set of a row is not an exposure of the lats: less than two effective sets did not really stimulate the muscle.
        var dose = LoadMetrics.Dose([new DoseWorkout(Now, [new DoseExercise("one-arm-seated-cable-row", ["Lats"], ["Biceps"], 1)])]);
        Assert.Equal(0m, dose.Single(d => d.Muscle == "Lats").Exposures);
        Assert.Equal(1m, dose.Single(d => d.Muscle == "Lats").Sets);
    }

    [Fact]
    public void ThePrescribedDosePerSevenDaysDividesTheBlockByTheWeeksItSpans()
    {
        // Four periods of the same two sessions: per seven days it is one pair of sessions.
        var sessions = Enumerable.Repeat((IReadOnlyList<DoseExercise>)[Bench, Row], 8).ToList();
        var perWeek = LoadMetrics.PrescribedPerSevenDays(sessions, 4);
        Assert.Equal(8m, perWeek.Single(d => d.Muscle == "Chest").Sets);
        Assert.Equal(2m, perWeek.Single(d => d.Muscle == "Chest").Exposures);
        Assert.Empty(LoadMetrics.PrescribedPerSevenDays(sessions, 0));
    }
}
