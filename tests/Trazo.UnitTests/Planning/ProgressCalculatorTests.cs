using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ProgressCalculatorTests
{
    // Monday 2026-11-16.
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly Dictionary<string, ProgressExerciseInfo> Info = new()
    {
        ["bench"] = new("Press banca", ["Chest"], ["Triceps"]),
        ["squat"] = new("Sentadilla", ["Quads"], ["Glutes"])
    };

    private static ProgressSetRow Set(string exercise, Guid log, DateOnly day, decimal? kg, int? reps) =>
        new(exercise, log, new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), kg, reps);

    private static ProgressView Build(params ProgressSetRow[] rows) =>
        ProgressCalculator.Build(rows, Info, TimeZoneInfo.Utc, Today, weeklyGoal: 4);

    [Fact]
    public void EpleyEstimateIsOnlyGivenUpToTwelveRepetitions()
    {
        Assert.Equal(100m, ProgressCalculator.EstimatedOneRepMax(100m, 1));
        Assert.Equal(133.3m, ProgressCalculator.EstimatedOneRepMax(100m, 10));
        Assert.Null(ProgressCalculator.EstimatedOneRepMax(100m, 13));
        Assert.Null(ProgressCalculator.EstimatedOneRepMax(0m, 5));
    }

    [Fact]
    public void NoHistoryGivesAnEmptyButValidProgress()
    {
        var view = Build();
        Assert.Equal(0, view.Summary.TotalSessions);
        Assert.Equal(0, view.Summary.StreakWeeks);
        Assert.Empty(view.Exercises);
        Assert.Equal(ProgressCalculator.Weeks, view.WeekStarts.Count);
        Assert.Equal(Today, view.WeekStarts[^1]);
    }

    [Fact]
    public void AnExerciseKeepsOnePointPerWorkout_AndFlagsANewRecord()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var view = Build(
            Set("bench", a, Today.AddDays(-7), 60m, 8), Set("bench", a, Today.AddDays(-7), 60m, 8),
            Set("bench", b, Today, 65m, 8));

        var bench = Assert.Single(view.Exercises);
        Assert.Equal(2, bench.Sessions);
        Assert.Equal(65m, bench.BestWeightKg);
        Assert.True(bench.NewRecord);
        Assert.Equal(Today, bench.RecordDate);
        Assert.Equal(2, bench.Points[0].Sets);
        Assert.Equal(960m, bench.Points[0].VolumeKg);
    }

    [Fact]
    public void ALighterLatestWorkoutIsNotARecord()
    {
        var view = Build(Set("bench", Guid.NewGuid(), Today.AddDays(-7), 70m, 5), Set("bench", Guid.NewGuid(), Today, 60m, 5));
        var bench = Assert.Single(view.Exercises);
        Assert.False(bench.NewRecord);
        Assert.Equal(Today.AddDays(-7), bench.RecordDate);
    }

    [Fact]
    public void TheStreakCountsConsecutiveWeeksAndSurvivesAnEmptyCurrentWeek()
    {
        var thisWeekLog = Guid.NewGuid();
        var withCurrent = Build(
            Set("bench", Guid.NewGuid(), Today.AddDays(-14), 50m, 8), Set("bench", Guid.NewGuid(), Today.AddDays(-7), 50m, 8),
            Set("bench", thisWeekLog, Today, 50m, 8));
        Assert.Equal(3, withCurrent.Summary.StreakWeeks);
        Assert.Equal(1, withCurrent.Summary.SessionsThisWeek);

        var gap = Build(Set("bench", Guid.NewGuid(), Today.AddDays(-7), 50m, 8), Set("bench", Guid.NewGuid(), Today.AddDays(-21), 50m, 8));
        Assert.Equal(1, gap.Summary.StreakWeeks); // this week is still open; last week counts, the older one is cut by the gap
        Assert.Equal(4, gap.Summary.WeeklyGoal);
    }

    [Fact]
    public void MuscleVolumeCountsPrimarySetsFullAndSecondarySetsHalf()
    {
        var log = Guid.NewGuid();
        var view = Build(Set("bench", log, Today, 60m, 8), Set("bench", log, Today, 60m, 8), Set("squat", log, Today.AddDays(-7), 80m, 5));

        var chest = view.Muscles.Single(m => m.Muscle == "Chest");
        var triceps = view.Muscles.Single(m => m.Muscle == "Triceps");
        var quads = view.Muscles.Single(m => m.Muscle == "Quads");
        Assert.Equal(2m, chest.SetsByWeek[^1]);
        Assert.Equal(1m, triceps.SetsByWeek[^1]);
        Assert.Equal(1m, quads.SetsByWeek[^2]);
        Assert.Equal("Chest", view.Muscles[0].Muscle); // ordered by total volume
    }

    [Fact]
    public void BodyweightExercisesHaveNoEstimateButStillCount()
    {
        var view = Build(Set("bench", Guid.NewGuid(), Today, null, 12));
        var e = Assert.Single(view.Exercises);
        Assert.Null(e.BestEstimatedOneRepMaxKg);
        Assert.Equal(12, e.Points[0].RepsAtTop);
        Assert.Equal(1, view.Summary.TotalSessions);
    }
}
