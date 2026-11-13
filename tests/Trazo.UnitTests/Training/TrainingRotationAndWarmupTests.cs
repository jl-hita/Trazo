using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingRotationAndWarmupTests
{
    private static readonly DayOfWeek[] Days = [DayOfWeek.Monday, DayOfWeek.Thursday];

    private static TrainingExerciseCandidate Squat(string id, int priority) => new(
        id, "catalog-test", $"Sentadilla {id}", TrainingMovementPattern.Squat, [],
        [TrainingGoal.Hypertrophy], TrainingExperience.Beginner, priority, true,
        [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]);

    private static TrainingExerciseCandidate[] CatalogWithSquatVariants() =>
        TrainingBlockProgressionTests.Catalog()
            .Where(c => c.MovementPattern != TrainingMovementPattern.Squat)
            .Concat([Squat("squat-a", 80), Squat("squat-b", 70), Squat("squat-c", 65), Squat("squat-weak", 10)])
            .ToArray();

    private static string SquatUsed(int rotationIndex)
    {
        var result = TrainingProgramGenerator.Generate(
            new TrainingProgramGenerationRequest(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, new DateOnly(2026, 11, 9), 2,
                Days, 90, [], [], null, false, TrainingProgramSplit.FullBody, (int?)rotationIndex),
            CatalogWithSquatVariants());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Prescription!.Sessions.First(s => s.WeekNumber == 1).Exercises
            .First(e => e.ExerciseId.StartsWith("squat-", StringComparison.Ordinal)).ExerciseId;
    }

    [Fact]
    public void IndexZeroKeepsTheBestRatedExercise() => Assert.Equal("squat-a", SquatUsed(0));

    [Fact]
    public void HigherIndexesRotateAmongTheBestSuitedExercisesOnly()
    {
        var used = Enumerable.Range(0, 6).Select(SquatUsed).ToArray();
        Assert.Equal(["squat-a", "squat-b", "squat-c", "squat-a", "squat-b", "squat-c"], used);
        Assert.DoesNotContain("squat-weak", used);
    }

    [Fact]
    public void ExerciseStaysConstantInsideOneBlock()
    {
        var result = TrainingProgramGenerator.Generate(
            new(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, new DateOnly(2026, 11, 9), 4,
                Days, 90, [], [], null, false, TrainingProgramSplit.FullBody, 1),
            CatalogWithSquatVariants());
        Assert.True(result.Succeeded);
        var perWeek = result.Prescription!.Sessions.GroupBy(s => s.WeekNumber)
            .Select(g => g.SelectMany(s => s.Exercises).Select(e => e.ExerciseId).OrderBy(x => x).ToArray()).ToArray();
        Assert.All(perWeek, ids => Assert.Equal(perWeek[0], ids));
    }

    [Fact]
    public void WarmupIsOnlyAddedWhereAPrimaryMuscleIsStillCold()
    {
        // Press banca (pecho) -> aperturas (pecho) -> elevaciones laterales (deltoides).
        var bench = Ex("bench", "Press banca", 1, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]);
        var fly = Ex("fly", "Aperturas en polea", 2, [TrainingMuscleGroup.Chest], []);
        var lateral = Ex("lateral", "Elevaciones laterales", 3, [TrainingMuscleGroup.Deltoids], []);
        var session = new TrainingSessionPrescription(1, DayOfWeek.Monday, "Empuje", 60, [bench, fly, lateral]);

        var result = TrainingProgramGenerator.AddWarmupGuidanceForTesting([session]);
        var exercises = result.Single().Exercises;

        Assert.Equal(2, exercises[0].WarmupSets);
        Assert.Null(exercises[1].WarmupSets);
        Assert.Equal(1, exercises[2].WarmupSets);
        Assert.Contains("Deltoids", exercises[2].WarmupNote);
        Assert.Equal(new[] { 3, 3, 3 }, exercises.Select(e => e.Sets));
    }

    [Fact]
    public void GeneratedPlansCarryWarmupGuidanceWithoutChangingWorkingSets()
    {
        var plan = TrainingBlockProgressionTests.Generate(1, TrainingGoal.Hypertrophy, 2, 90);
        Assert.All(plan.Sessions, session =>
        {
            Assert.Equal(2, session.Exercises[0].WarmupSets);
            Assert.All(session.Exercises, e => Assert.InRange(e.WarmupSets ?? 0, 0, 2));
        });
        Assert.Empty(TrainingPrescriptionValidator.Validate(plan));
    }

    [Fact]
    public void ValidatorRejectsOutOfRangeWarmupSets()
    {
        var plan = TrainingBlockProgressionTests.Generate(1, TrainingGoal.Hypertrophy, 2, 90);
        var bad = plan with
        {
            Sessions = plan.Sessions.Select(s => s with
            {
                Exercises = s.Exercises.Select(e => e with { WarmupSets = 4 }).ToArray()
            }).ToArray()
        };
        Assert.Contains(TrainingPrescriptionValidator.Validate(bad), e => e.Contains("aproximación"));
    }

    private static TrainingExercisePrescription Ex(
        string id, string name, int order, TrainingMuscleGroup[] primary, TrainingMuscleGroup[] secondary) =>
        new(id, "catalog-test", name, order, 3, 8, 12, 2m, 90, TrainingProgressionRule.DoubleProgression, "Prueba.", primary, secondary);
}
