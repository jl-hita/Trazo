using System.Text.Json.Nodes;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingExerciseSwapTests
{
    private static TrainingExerciseCandidate C(
        string id, TrainingMovementPattern pattern, int priority, TrainingMuscleGroup[] primary,
        string[]? equipment = null, TrainingExperience min = TrainingExperience.Beginner,
        TrainingGoal[]? goals = null, bool reviewed = true) =>
        new(id, "v1", id, pattern, equipment ?? [], goals ?? [TrainingGoal.Hypertrophy], min, priority, reviewed, primary, []);

    private static readonly TrainingMuscleGroup[] Chest = [TrainingMuscleGroup.Chest];

    private static TrainingAlternativeResult Find(
        IReadOnlyCollection<TrainingExerciseCandidate> catalog, string[]? excluded = null,
        TrainingExperience exp = TrainingExperience.Intermediate, string[]? equipment = null) =>
        TrainingExerciseAlternativeFinder.Find("bench", Chest, catalog,
            (excluded ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase), TrainingGoal.Hypertrophy, exp, equipment);

    [Fact]
    public void PrefersSamePatternRankedByOverlapThenPriorityAndFlagsTheRotationPool()
    {
        var result = Find([
            C("bench", TrainingMovementPattern.HorizontalPush, 80, Chest),
            C("dumbbell-press", TrainingMovementPattern.HorizontalPush, 70, Chest),
            C("machine-press", TrainingMovementPattern.HorizontalPush, 60, Chest),
            C("old-press", TrainingMovementPattern.HorizontalPush, 10, Chest),
            C("squat", TrainingMovementPattern.Squat, 90, [TrainingMuscleGroup.Quadriceps])]);
        Assert.Equal(["dumbbell-press", "machine-press", "old-press"], result.Alternatives.Select(a => a.ExerciseId));
        Assert.All(result.Alternatives, a => Assert.Equal("pattern", a.MatchLevel));
        Assert.True(result.Alternatives[0].InRotationPool);
        Assert.False(result.Alternatives[2].InRotationPool);
        Assert.Null(result.Message);
    }

    [Fact]
    public void RespectsExperienceGoalExclusionsReviewAndEquipment()
    {
        var result = Find([
            C("bench", TrainingMovementPattern.HorizontalPush, 80, Chest),
            C("advanced-only", TrainingMovementPattern.HorizontalPush, 90, Chest, min: TrainingExperience.Advanced),
            C("wrong-goal", TrainingMovementPattern.HorizontalPush, 90, Chest, goals: [TrainingGoal.Strength]),
            C("excluded", TrainingMovementPattern.HorizontalPush, 90, Chest),
            C("pending", TrainingMovementPattern.HorizontalPush, 90, Chest, reviewed: false),
            C("barbell-only", TrainingMovementPattern.HorizontalPush, 90, Chest, equipment: ["barbell"]),
            C("pushup", TrainingMovementPattern.HorizontalPush, 50, Chest, equipment: [])],
            excluded: ["excluded"], equipment: ["dumbbell"]);
        Assert.Equal(["pushup"], result.Alternatives.Select(a => a.ExerciseId));
    }

    [Fact]
    public void FallsBackToSameMusclesAnyPatternOnlyWhenThereIsNoPatternMatch()
    {
        var result = Find([
            C("bench", TrainingMovementPattern.HorizontalPush, 80, Chest),
            C("cable-fly", TrainingMovementPattern.Accessory, 60, Chest)]);
        var alt = Assert.Single(result.Alternatives);
        Assert.Equal("cable-fly", alt.ExerciseId);
        Assert.Equal("muscles", alt.MatchLevel);
    }

    [Fact]
    public void WarnsExplicitlyWhenNothingSuitableExists()
    {
        var result = Find([
            C("bench", TrainingMovementPattern.HorizontalPush, 80, Chest),
            C("squat", TrainingMovementPattern.Squat, 90, [TrainingMuscleGroup.Quadriceps])]);
        Assert.Empty(result.Alternatives);
        Assert.Equal(TrainingExerciseAlternativeFinder.NoAlternativeMessage, result.Message);
    }

    private const string Session = """{"exercises":[{"exerciseId":"bench","catalogVersion":"v0","exerciseNameSnapshot":"Press banca","order":1,"sets":3,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":2,"restSeconds":120,"progressionRule":1,"targetLoadKg":80,"loadBasis":"x","warmupSets":2,"warmupNote":"y","primaryMuscleGroups":["Chest"],"secondaryMuscleGroups":["Triceps"],"selectionRationale":"r"},{"exerciseId":"row","exerciseNameSnapshot":"Remo","order":2,"sets":3,"minRepetitions":8,"maxRepetitions":12,"restSeconds":90,"progressionRule":1,"catalogVersion":"v0"}]}""";

    [Fact]
    public void SwapKeepsPrescriptionNumbersDropsLoadAndRecordsTheOrigin()
    {
        var replacement = C("dumbbell-press", TrainingMovementPattern.HorizontalPush, 70, Chest);
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(Session, "bench", replacement, out var updated, out var legacy));
        var ex = JsonNode.Parse(updated)!["exercises"]![0]!;
        Assert.Equal("dumbbell-press", (string)ex["exerciseId"]!);
        Assert.Equal("bench", (string)ex["swappedFromExerciseId"]!);
        Assert.Equal(3, (int)ex["sets"]!);
        Assert.Equal(120, (int)ex["restSeconds"]!);
        Assert.Null(ex["targetLoadKg"]);
        Assert.Null(ex["warmupSets"]);
        Assert.Contains("Intercambio solicitado por el paciente", (string)ex["selectionRationale"]!);
        Assert.Contains("dumbbell-press", legacy);

        // swapping again keeps the very first origin
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(
            updated, "dumbbell-press", C("machine-press", TrainingMovementPattern.HorizontalPush, 60, Chest), out var twice, out _));
        Assert.Equal("bench", (string)JsonNode.Parse(twice)!["exercises"]![0]!["swappedFromExerciseId"]!);
    }

    [Fact]
    public void SwapFailsForMissingExerciseOrReplacementAlreadyInTheSession()
    {
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(
            Session, "nope", C("x", TrainingMovementPattern.Squat, 50, Chest), out _, out _));
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(
            Session, "bench", C("row", TrainingMovementPattern.HorizontalPull, 50, Chest), out _, out _));
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(
            "not json", "bench", C("x", TrainingMovementPattern.Squat, 50, Chest), out _, out _));
    }
}
