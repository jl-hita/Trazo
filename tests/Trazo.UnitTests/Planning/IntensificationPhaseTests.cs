using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class IntensificationPhaseTests
{
    private static TrainingExercisePrescription Lift(int min, int max, decimal? rir, decimal? load, int rest = 180) => new(
        "x", "1", "X", 1, 4, min, max, rir, rest, TrainingProgressionRule.DoubleProgression, "t", [TrainingMuscleGroup.Chest], [],
        SmallestLoadIncrementKg: 2.5m, TargetLoadKg: load);

    [Fact]
    public void HeavyLiftsMoveToLowerRepsNearerFailureWithMoreRestAndAHeavierLoad()
    {
        var result = IntensificationPhase.Apply(Lift(3, 6, 2m, 100m));

        Assert.Equal(2, result.MinRepetitions);
        Assert.Equal(4, result.MaxRepetitions);
        Assert.Equal(1m, result.TargetRepetitionsInReserve);
        Assert.Equal(210, result.RestSeconds);
        Assert.True(result.TargetLoadKg > 100m && result.TargetLoadKg <= 108m);
        Assert.Equal(0m, result.TargetLoadKg!.Value % 2.5m);
        Assert.Contains("intensificación", result.LoadBasis);
    }

    [Fact]
    public void AccessoriesKeepTheirRangeAndLoad()
    {
        var accessory = Lift(8, 12, 2m, 20m, rest: 90);
        Assert.Equal(accessory, IntensificationPhase.Apply(accessory));
    }

    [Fact]
    public void ThereIsNeverARangeBelowTwoOrAnInvertedOne_AndRestIsCapped()
    {
        var result = IntensificationPhase.Apply(Lift(2, 3, 1m, null, rest: 290));
        Assert.InRange(result.MinRepetitions, 2, result.MaxRepetitions);
        Assert.True(result.MaxRepetitions <= 3);
        Assert.Equal(1m, result.TargetRepetitionsInReserve);
        Assert.Equal(300, result.RestSeconds);
        Assert.Null(result.TargetLoadKg);
    }

    [Fact]
    public void AnIntensificationBlockOfAStrengthPlanLowersTheHeavyLiftsComparedWithAccumulation()
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Strength, TrainingExperience.Advanced, 4,
            ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"], null, null);
        var catalog = PlanTestCatalog.Candidates();

        var accumulation = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), catalog);
        var intensification = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "intensification", 1, 4), catalog);

        int Heaviest(MesocycleGeneration g) => g.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Min(e => e.MaxRepetitions);
        Assert.True(accumulation.Succeeded && intensification.Succeeded);
        Assert.True(Heaviest(intensification) < Heaviest(accumulation));
    }
}
