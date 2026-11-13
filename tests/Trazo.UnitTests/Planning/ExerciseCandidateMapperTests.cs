using Trazo.Application.Planning;
using Trazo.Domain.Training;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseCandidateMapperTests
{
    internal static ExerciseCatalogItem Item(
        string id = "bench-press", string pattern = "HorizontalPush", string experience = "Beginner",
        string[]? goals = null, string[]? primary = null, string[]? secondary = null, string[]? equipment = null,
        bool active = true, int priority = 70) => new()
    {
        Id = id, Source = "trazo", NameEn = id, NameEs = $"Ejercicio {id}", DescriptionEs = "x", ForceType = "push",
        Mechanic = "compound", Difficulty = "beginner", BodyPart = "chest", MovementPattern = pattern,
        MinimumExperience = experience, SuitableGoals = goals ?? ["GeneralFitness", "Hypertrophy", "Strength"],
        PrimaryMuscles = primary ?? ["Chest"], SecondaryMuscles = secondary ?? ["Triceps"],
        RequiredEquipment = equipment ?? ["barbell", "bench"], SelectionPriority = priority, IsActive = active,
        SeedVersion = 1, UpdatedAt = new DateTimeOffset(2026, 11, 13, 10, 30, 0, TimeSpan.Zero)
    };

    [Fact]
    public void MapsTheCatalogVocabularyOntoTheEngineCandidate()
    {
        var result = ExerciseCandidateMapper.Map([Item(goals: ["Hypertrophy", "Strength"])]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Empty(result.Skipped);
        Assert.Equal("bench-press", candidate.ExerciseId);
        Assert.Equal("1-202611131030", candidate.CatalogVersion);
        Assert.Equal("Ejercicio bench-press", candidate.Name);
        Assert.Equal(TrainingMovementPattern.HorizontalPush, candidate.MovementPattern);
        Assert.Equal(TrainingExperience.Beginner, candidate.MinimumExperience);
        Assert.Equal([TrainingGoal.Hypertrophy, TrainingGoal.Strength], candidate.SuitableGoals);
        Assert.Equal([TrainingMuscleGroup.Chest], candidate.PrimaryMuscleGroups);
        Assert.Equal([TrainingMuscleGroup.Triceps], candidate.SecondaryMuscleGroups);
        Assert.Equal(["barbell", "bench"], candidate.RequiredEquipment);
        Assert.True(candidate.IsReviewed);
    }

    [Fact]
    public void SkipsExercisesTheEngineCannotUse_AndSaysWhy()
    {
        var result = ExerciseCandidateMapper.Map([
            Item("hidden", active: false),
            Item("rowing", pattern: "Cardio"),
            Item("odd-level", experience: "Elite"),
            Item("no-goal", goals: ["Endurance"]),
            Item("no-muscle", primary: ["Wings"]),
            Item("fine")]);

        Assert.Equal(["fine"], result.Candidates.Select(c => c.ExerciseId));
        Assert.Equal(["hidden", "rowing", "odd-level", "no-goal", "no-muscle"], result.Skipped.Select(s => s.ExerciseId));
        Assert.Contains("Cardio", result.Skipped.Single(s => s.ExerciseId == "rowing").Reason);
    }

    [Fact]
    public void DropsUnknownValuesInsteadOfGuessing()
    {
        var candidate = Assert.Single(ExerciseCandidateMapper.Map([Item(
            goals: ["Strength", "Endurance"], primary: ["Chest", "Wings"], secondary: ["Chest", "Triceps", "Gills"])]).Candidates);

        Assert.Equal([TrainingGoal.Strength], candidate.SuitableGoals);
        Assert.Equal([TrainingMuscleGroup.Chest], candidate.PrimaryMuscleGroups);
        Assert.Equal([TrainingMuscleGroup.Triceps], candidate.SecondaryMuscleGroups); // not repeated from the primary list
    }

    [Fact]
    public void IgnoresNumericStringsThatWouldParseAsEnumValues()
    {
        var result = ExerciseCandidateMapper.Map([Item(pattern: "1"), Item("goal-number", goals: ["2"])]);

        Assert.Empty(result.Candidates);
        Assert.Equal(2, result.Skipped.Count);
    }
}
