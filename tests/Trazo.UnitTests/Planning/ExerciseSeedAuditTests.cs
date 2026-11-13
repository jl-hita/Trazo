using System.Text.Json;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Keeps the curated seed, its illustrations and the engine's stimulus table in step: every seed exercise needs an
/// explicit stimulus decision, a seeded exercise folder must belong to an exercise, and no exercise may require a
/// weight belt.
/// </summary>
public sealed class ExerciseSeedAuditTests
{
    private static readonly string SeedsDirectory = FindSeedsDirectory();

    private static string FindSeedsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Trazo.Infrastructure", "Seeds")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Trazo.Infrastructure", "Seeds");
    }

    private static List<JsonElement> Entries()
    {
        using var stream = File.OpenRead(Path.Combine(SeedsDirectory, "exercise-catalog-additions.json"));
        return JsonDocument.Parse(stream).RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static IEnumerable<string> Names(JsonElement entry, string property) =>
        entry.GetProperty(property).EnumerateArray().Select(x => x.GetString()!);

    [Fact]
    public void EverySeedExerciseHasAnExplicitStimulusDecision()
    {
        var missing = Entries().Select(x => x.GetProperty("id").GetString()!).Where(id => !TrainingExerciseStimulus.IsReviewed(id)).ToList();

        Assert.True(missing.Count == 0, "Add these exercises to TrainingExerciseStimulus: " + string.Join(", ", missing));
    }

    [Fact]
    public void MeaningfulSecondaryMusclesCountAsSetsOnlyWhereTheTableSaysSo()
    {
        var row = TrainingExerciseStimulus.GetEffectiveMuscles("pendlay-row", [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack],
            [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Forearms]);
        Assert.Contains(TrainingMuscleGroup.Biceps, row);
        Assert.DoesNotContain(TrainingMuscleGroup.ErectorSpinae, row);

        var press = TrainingExerciseStimulus.GetEffectiveMuscles("pike-push-up", [TrainingMuscleGroup.Deltoids],
            [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.UpperBack]);
        Assert.Contains(TrainingMuscleGroup.Triceps, press);
        Assert.DoesNotContain(TrainingMuscleGroup.UpperBack, press);

        var hinge = TrainingExerciseStimulus.GetEffectiveMuscles("rack-pull", [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Glutes],
            [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Forearms]);
        Assert.Contains(TrainingMuscleGroup.Hamstrings, hinge);

        var calves = TrainingExerciseStimulus.GetEffectiveMuscles("dumbbell-front-raise", [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps]);
        Assert.Equal([TrainingMuscleGroup.Deltoids], calves);
    }

    [Fact]
    public void NoSeedExerciseRequiresAWeightBelt()
    {
        var offenders = Entries().Where(x => Names(x, "requiredEquipment").Contains("weight-belt")).Select(x => x.GetProperty("id").GetString()).ToList();

        Assert.Empty(offenders);
        var weighted = Entries().Where(x => x.GetProperty("id").GetString() is "weighted-pull-up" or "weighted-dip").ToList();
        Assert.Equal(2, weighted.Count);
        Assert.Equal(["pull-up-bar"], Names(weighted.Single(x => x.GetProperty("id").GetString() == "weighted-pull-up"), "requiredEquipment"));
        Assert.Equal(["dip-station"], Names(weighted.Single(x => x.GetProperty("id").GetString() == "weighted-dip"), "requiredEquipment"));
    }

    [Fact]
    public void EverySeededImageFolderBelongsToASeedExercise()
    {
        var ids = Entries().Select(x => x.GetProperty("id").GetString()!).ToHashSet();
        var root = Path.Combine(SeedsDirectory, "exercise-images");
        var folders = Directory.Exists(root) ? Directory.GetDirectories(root) : [];

        foreach (var folder in folders)
            Assert.Contains(Path.GetFileName(folder), ids);
    }

    [Fact]
    public void EveryNewExerciseHasEnoughInstructionsAndAValidPriority()
    {
        foreach (var entry in Entries())
        {
            var id = entry.GetProperty("id").GetString();
            Assert.True(entry.GetProperty("instructionsEs").GetArrayLength() >= 3, id);
            Assert.InRange(entry.GetProperty("selectionPriority").GetInt32(), 0, 100);
            Assert.NotEmpty(Names(entry, "primaryMuscles"));
        }
    }
}
