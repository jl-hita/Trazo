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
        ["squat"] = new("Sentadilla", ["Quadriceps"], ["Glutes"])
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
    public void AMuscleThatReallyWorksCountsEverySet_AsInTheRestOfTheApp()
    {
        var log = Guid.NewGuid();
        var view = Build(Set("bench", log, Today, 60m, 8), Set("bench", log, Today, 60m, 8), Set("squat", Guid.NewGuid(), Today.AddDays(-7), 80m, 5));

        var chest = view.Muscles.Single(m => m.Muscle == "Chest");
        var triceps = view.Muscles.Single(m => m.Muscle == "Triceps");
        var quads = view.Muscles.Single(m => m.Muscle == "Quadriceps");
        Assert.Equal(2m, chest.SetsByWeek[^1]);
        Assert.Equal(2m, triceps.SetsByWeek[^1]);
        Assert.Equal(1m, quads.SetsByWeek[^2]);
        Assert.Equal("Chest", view.Muscles[0].Muscle); // ordered by total volume
    }

    [Fact]
    public void WhatThePlanAskedStandsBesideWhatWasDone_AndFrequencyCountsRealWork()
    {
        var log = Guid.NewGuid();
        var program = new ProgressProgram(
            [(Today, [new DoseExercise("bench", ["Chest"], ["Triceps"], 4m)]), (Today.AddDays(2), [new DoseExercise("curl", ["Biceps"], [], 3m)])],
            [new MuscleDoseView("Chest", 8m, 2m)], new Dictionary<string, string> { ["Chest"] = "Emphasize" });
        var view = ProgressCalculator.Build(
            [Set("bench", log, Today, 60m, 8), Set("bench", log, Today, 60m, 8), Set("squat", Guid.NewGuid(), Today, 80m, 5)],
            Info, TimeZoneInfo.Utc, Today, 4, program);

        var chest = view.Muscles.Single(m => m.Muscle == "Chest");
        Assert.Equal((4m, 8m, "Emphasize"), (chest.PrescribedByWeek![^1]!.Value, chest.PrescribedPerSevenDays!.Value, chest.Priority));
        Assert.Equal(1, chest.SessionsByWeek![^1]);
        // One set is not enough to count as having worked the muscle.
        Assert.Equal(0, view.Muscles.Single(m => m.Muscle == "Quadriceps").SessionsByWeek![^1]);
        // Asked and not done is shown, with nothing done.
        var biceps = view.Muscles.Single(m => m.Muscle == "Biceps");
        Assert.Equal((0m, 3m), (biceps.SetsByWeek[^1], biceps.PrescribedByWeek![^1]!.Value));
    }

    [Fact]
    public void BlocksCompareLoadsOnlyOnSharedExercisesOfTrainingBlocks()
    {
        BlockFacts Block(int sequence, bool deload, params (string Id, decimal Kg)[] done) => new(
            sequence, deload ? "deload" : "accumulation", deload, sequence * 4 - 3, 4,
            [[new DoseExercise("bench", ["Chest"], [], 3m)]],
            done.Length == 0 ? [] : [done.Select(d => new DoseExercise(d.Id, ["Chest"], [], 1m)).ToList()],
            done.Select(d => Set(d.Id, Guid.NewGuid(), Today, d.Kg, 5)).ToList());

        var summaries = BlockSummaries.Build(
            [Block(1, false, ("bench", 100m), ("old", 50m)), Block(2, true, ("bench", 60m)), Block(3, false, ("bench", 105m), ("new", 20m))], id => id);

        Assert.Empty(summaries[0].Exercises);
        Assert.Empty(summaries[1].Exercises);
        var bench = Assert.Single(summaries[2].Exercises);
        Assert.Equal(("bench", 5m), (bench.ExerciseId, bench.ChangePercent));
        Assert.Equal((3m, 2m), (summaries[2].Muscles[0].PrescribedSets, summaries[2].Muscles[0].PerformedSets));
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
