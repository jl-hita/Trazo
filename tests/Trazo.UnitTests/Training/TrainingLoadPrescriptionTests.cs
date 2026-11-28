using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingLoadPrescriptionTests
{
    private static TrainingExecutedSet[] Sets(decimal kg, int reps, decimal? rir, int count = 3) =>
        Enumerable.Repeat(new TrainingExecutedSet("working", kg, reps, rir, true), count).ToArray();

    [Fact]
    public void NoHistoryMeansNoInventedLoad() =>
        Assert.Null(TrainingLoadEstimator.Suggest([], 8, 12, 2m));

    [Fact]
    public void PerformanceInsideTheRangeKeepsTheSameLoad()
    {
        var suggestion = TrainingLoadEstimator.Suggest(Sets(60m, 10, 2m), 8, 12, 2m)!;
        Assert.Equal(60m, suggestion.LoadKg);
        Assert.Contains("Misma carga", suggestion.Basis);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(18)]
    public void TheSameLoadIsNotRounded(int load) =>
        Assert.Equal(load, TrainingLoadEstimator.Suggest(Sets(load, 10, 2m), 8, 12, 2m)!.LoadKg);

    [Theory]
    [InlineData(37.5, 20)]
    [InlineData(92.5, 22)]
    [InlineData(22.5, 12)]
    public void AnIncreaseNeverPassesTheCapBecauseOfRounding(double load, int reps)
    {
        var suggestion = TrainingLoadEstimator.Suggest(Sets((decimal)load, reps, 2m), 5, 8, 2m, 2.5m)!;
        Assert.InRange(suggestion.LoadKg, (decimal)load, (decimal)load * 1.10m);
    }

    [Fact]
    public void TooFewRepetitionsReduceTheLoadWithinTheCap()
    {
        var suggestion = TrainingLoadEstimator.Suggest(Sets(100m, 4, 0m), 8, 12, 2m)!;
        Assert.InRange(suggestion.LoadKg, 90m, 100m); // cap: at most 10 % down
        Assert.True(suggestion.LoadKg < 100m);
        Assert.Contains("reducida", suggestion.Basis);
    }

    [Fact]
    public void RepetitionsAboveTheRangeIncreaseTheLoadWithinTheCapAndIncrement()
    {
        var suggestion = TrainingLoadEstimator.Suggest(Sets(40m, 15, 2m), 8, 12, 2m, 2.5m)!;
        Assert.True(suggestion.LoadKg > 40m && suggestion.LoadKg <= 45m);
        Assert.Equal(0m, suggestion.LoadKg % 2.5m);
    }

    [Fact]
    public void IncompleteSetsAreIgnored()
    {
        var sets = new[] { new TrainingExecutedSet("working", 50m, 10, 2m, false) };
        Assert.Null(TrainingLoadEstimator.Suggest(sets, 8, 12, 2m));
    }

    [Fact]
    public void GeneratorPrescribesWeekOneLoadsOnlyForExercisesWithHistory()
    {
        var catalog = TrainingBlockProgressionTests.Catalog();
        var request = new TrainingProgramGenerationRequest(
            TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, new DateOnly(2026, 11, 9), 2,
            [DayOfWeek.Monday, DayOfWeek.Thursday], 90, [], [], null, false, TrainingProgramSplit.FullBody, 0,
            [new TrainingLoadReference("ex-HorizontalPush", 80m, 10, 2m, 3)]);
        var result = TrainingProgramGenerator.Generate(request, catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var exercises = result.Prescription!.Sessions.SelectMany(s => s.Exercises).ToArray();
        var withLoad = exercises.Where(e => e.TargetLoadKg is not null).ToArray();
        Assert.NotEmpty(withLoad);
        Assert.All(withLoad, e =>
        {
            Assert.Equal("ex-HorizontalPush", e.ExerciseId);
            Assert.Equal(80m, e.TargetLoadKg);
            Assert.NotNull(e.SmallestLoadIncrementKg);
        });
        Assert.All(result.Prescription.Sessions.Where(s => s.WeekNumber == 2).SelectMany(s => s.Exercises),
            e => Assert.Null(e.TargetLoadKg));
        Assert.Empty(TrainingPrescriptionValidator.Validate(result.Prescription));
    }

    [Fact]
    public void WithoutReferencesNoLoadIsPrescribed()
    {
        var plan = TrainingBlockProgressionTests.Generate(2);
        Assert.All(plan.Sessions.SelectMany(s => s.Exercises), e => Assert.Null(e.TargetLoadKg));
    }
}
