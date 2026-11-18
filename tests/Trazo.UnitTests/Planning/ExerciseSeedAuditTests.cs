using System.Text.Json;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

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
        using var stream = File.OpenRead(Path.Combine(SeedsDirectory, "exercise-catalog.json"));
        return JsonDocument.Parse(stream).RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static IEnumerable<string> Names(JsonElement entry, string property) =>
        entry.GetProperty(property).EnumerateArray().Select(x => x.GetString()!);

    [Fact]
    public void SeedIsACompleteSnapshotOfTheCurrentExerciseLibrary()
    {
        var entries = Entries();
        Assert.Equal(148, entries.Count);
        Assert.Equal(148, entries.Select(x => x.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(100, entries.Count(x => x.GetProperty("source").GetString() == "repdb-free"));
        Assert.Equal(48, entries.Count(x => x.GetProperty("source").GetString() == "trazo-curated"));
        Assert.All(entries, x => Assert.True(x.GetProperty("isActive").GetBoolean()));
    }

    [Fact]
    public void EveryCuratedSeedExerciseHasAnExplicitStimulusDecision()
    {
        var missing = Entries()
            .Where(x => x.GetProperty("source").GetString() == "trazo-curated")
            .Select(x => x.GetProperty("id").GetString()!)
            .Where(id => !TrainingExerciseStimulus.IsReviewed(id))
            .ToList();

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
        Assert.Equal(["pull-up-bar", "dumbbell"], Names(weighted.Single(x => x.GetProperty("id").GetString() == "weighted-pull-up"), "requiredEquipment"));
        Assert.Equal(["dip-station", "dumbbell"], Names(weighted.Single(x => x.GetProperty("id").GetString() == "weighted-dip"), "requiredEquipment"));
    }

    [Fact]
    public void RequiredEquipmentNeverUsesGenericMachineAndCapturesSpecificDependencies()
    {
        var entries = Entries();
        var genericMachine = entries
            .Where(x => Names(x, "requiredEquipment").Any(value => string.Equals(value, "machine", StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.GetProperty("id").GetString())
            .ToList();
        Assert.Empty(genericMachine);

        Assert.Equal(["barbell", "bench"], Names(entries.Single(x => x.GetProperty("id").GetString() == "chest-supported-t-bar-row"), "requiredEquipment"));
        Assert.Equal(["pull-up-bar", "dumbbell"], Names(entries.Single(x => x.GetProperty("id").GetString() == "weighted-pull-up"), "requiredEquipment"));
        Assert.Equal(["dip-station", "dumbbell"], Names(entries.Single(x => x.GetProperty("id").GetString() == "weighted-dip"), "requiredEquipment"));
        Assert.Equal(["step"], Names(entries.Single(x => x.GetProperty("id").GetString() == "elevated-pike-push-up"), "requiredEquipment"));
        Assert.Equal(["barbell", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "rack-pull"), "requiredEquipment"));
        Assert.Equal(["barbell", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "pause-squat"), "requiredEquipment"));
        Assert.Equal(["barbell", "bench", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "barbell-bulgarian-split-squat"), "requiredEquipment"));
        Assert.Equal(["low-row-bar"], Names(entries.Single(x => x.GetProperty("id").GetString() == "bent-knee-inverted-row"), "requiredEquipment"));
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
