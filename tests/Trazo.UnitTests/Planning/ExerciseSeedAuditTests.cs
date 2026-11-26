using System.Text.Json;
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
        Assert.Equal(150, entries.Count);
        Assert.Equal(150, entries.Select(x => x.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(100, entries.Count(x => x.GetProperty("source").GetString() == "repdb-free"));
        Assert.Equal(50, entries.Count(x => x.GetProperty("source").GetString() == "trazo-curated"));
        Assert.Equal(["standing-calf-raise"], entries.Where(x => !x.GetProperty("isActive").GetBoolean()).Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public void EverySeedExerciseIsReviewedAndStatesItsPlanningMetadata()
    {
        Assert.All(Entries(), x =>
        {
            Assert.True(x.GetProperty("metadataReviewed").GetBoolean(), x.GetProperty("id").GetString());
            Assert.True(x.TryGetProperty("effectiveSecondaryMuscles", out _) && x.TryGetProperty("emphasis", out _) &&
                x.TryGetProperty("limitationAreas", out _), x.GetProperty("id").GetString());
        });
    }

    [Theory]
    [InlineData("pendlay-row", "Biceps", "ErectorSpinae")]
    [InlineData("pike-push-up", "Triceps", "UpperBack")]
    [InlineData("rack-pull", "Hamstrings", "Forearms")]
    public void ASecondaryMuscleCountsAsASetOnlyWhereTheCatalogSaysSo(string id, string counts, string incidental)
    {
        var entry = Entries().Single(x => x.GetProperty("id").GetString() == id);

        Assert.Contains(counts, Names(entry, "effectiveSecondaryMuscles"));
        Assert.DoesNotContain(incidental, Names(entry, "effectiveSecondaryMuscles"));
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

        Assert.Equal(["barbell", "landmine", "t-bar-row-handle", "bench"], Names(entries.Single(x => x.GetProperty("id").GetString() == "chest-supported-t-bar-row"), "requiredEquipment"));
        Assert.Equal(["pull-up-bar", "dumbbell"], Names(entries.Single(x => x.GetProperty("id").GetString() == "weighted-pull-up"), "requiredEquipment"));
        Assert.Equal(["dip-station", "dumbbell"], Names(entries.Single(x => x.GetProperty("id").GetString() == "weighted-dip"), "requiredEquipment"));
        Assert.Equal(["step"], Names(entries.Single(x => x.GetProperty("id").GetString() == "elevated-pike-push-up"), "requiredEquipment"));
        Assert.Equal(["barbell", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "rack-pull"), "requiredEquipment"));
        Assert.Equal(["barbell", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "pause-squat"), "requiredEquipment"));
        Assert.Equal(["barbell", "bench", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "barbell-bulgarian-split-squat"), "requiredEquipment"));
        Assert.Equal(["barbell", "squat-rack"], Names(entries.Single(x => x.GetProperty("id").GetString() == "bent-knee-inverted-row"), "requiredEquipment"));
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

    // ---- Experience classification. The level is DATA about the exercise (technical demand, prerequisite strength or mobility,
    // consequence of failure), never an engine exception: the generator only compares it with the user's level.

    private static string Level(string id) =>
        Entries().Single(x => x.GetProperty("id").GetString() == id).GetProperty("minimumExperience").GetString()!;

    [Fact]
    public void FundamentalLiftsWithAGuidedOrSupportedPathAreOpenToBeginners()
    {
        // bench-press and db-bench-press were Intermediate in production, so beginners never received them.
        foreach (var id in new[] { "bench-press", "db-bench-press", "incline-db-press", "seated-cable-row", "one-arm-seated-cable-row", "hack-squat" })
            Assert.True(Level(id) == "Beginner", $"{id} must be open to beginners");
        foreach (var id in new[] { "bench-press", "db-bench-press" })
            Assert.Contains("Hypertrophy", Names(Entries().Single(x => x.GetProperty("id").GetString() == id), "suitableGoals"));
    }

    [Fact]
    public void SiblingVariantsOfTheSameMovementShareTheirLevel()
    {
        string[][] families =
        [
            ["seated-cable-row", "one-arm-seated-cable-row", "wide-grip-seated-cable-row", "kneeling-cable-row"],
            ["cable-lateral-raise", "lateral-raise", "seated-dumbbell-lateral-raise"],
            ["cable-overhead-tricep-extension", "overhead-tricep-extension"],
            ["bench-press", "db-bench-press", "smith-machine-bench-press"],
            ["hack-squat", "leg-press", "horizontal-leg-press"]
        ];
        foreach (var family in families)
            Assert.True(family.Select(Level).Distinct().Count() == 1, "Inconsistent levels in " + string.Join(", ", family.Select(id => $"{id}={Level(id)}")));
    }

    [Fact]
    public void TheLevelsWereNotRelaxedIndiscriminately()
    {
        // Loaded spinal patterns, balance-limited or strength-gated exercises keep their minimum level.
        foreach (var id in new[] { "squat", "front-squat", "deadlift", "sumo-deadlift", "romanian-deadlift", "barbell-row", "t-bar-row", "ohp", "pull-up", "chin-ups", "parallel-bar-dips", "bulgarian-split-squat", "incline-bench-press", "close-grip-bench-press" })
            Assert.True(Level(id) == "Intermediate", $"{id} must stay Intermediate");
        foreach (var id in new[] { "pendlay-row", "rack-pull", "weighted-pull-up", "weighted-dip", "paused-bench-press", "push-press", "pistol-squat" })
            Assert.True(Level(id) == "Advanced", $"{id} must stay Advanced");

        var counts = Entries().GroupBy(x => x.GetProperty("minimumExperience").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(102, counts["Beginner"]);
        Assert.Equal(30, counts["Intermediate"]);
        Assert.Equal(18, counts["Advanced"]);
    }

    [Fact]
    public void BeginnersHaveSeveralCompoundOptionsForEveryMainPattern()
    {
        foreach (var pattern in new[] { "Squat", "HorizontalPush", "HorizontalPull", "VerticalPull", "VerticalPush" })
        {
            var options = Entries().Count(x => x.GetProperty("movementPattern").GetString() == pattern
                && x.GetProperty("minimumExperience").GetString() == "Beginner" && x.GetProperty("mechanic").GetString() == "compound");
            Assert.True(options >= 3, $"{pattern} has only {options} beginner compound options");
        }
    }

    [Fact]
    public void ChangedSeedRowsCarryANewVersionSoUntouchedProductionRowsAreRefreshed()
    {
        // The seeder only refreshes untouched rows whose SeedVersion is lower than the seed's.
        foreach (var id in new[] { "bench-press", "db-bench-press", "incline-db-press", "seated-cable-row", "one-arm-seated-cable-row", "hack-squat",
                     "cable-fly", "cable-lateral-raise", "cable-reverse-fly", "dumbbell-reverse-fly", "cable-overhead-tricep-extension", "straight-arm-pulldown" })
            Assert.True(Entries().Single(x => x.GetProperty("id").GetString() == id).GetProperty("version").GetInt32() >= 3, id);
    }

    [Fact]
    public void EveryExerciseNameIsUniqueAndPresentInBothLanguages()
    {
        var entries = Entries();
        Assert.All(entries, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("nameEs").GetString()), x.GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("nameEn").GetString()), x.GetProperty("id").GetString());
        });
        Assert.Equal(entries.Count, entries.Select(x => x.GetProperty("nameEs").GetString()!.Trim().ToLowerInvariant()).Distinct().Count());
        Assert.Equal(entries.Count, entries.Select(x => x.GetProperty("nameEn").GetString()!.Trim().ToLowerInvariant()).Distinct().Count());
    }
}
