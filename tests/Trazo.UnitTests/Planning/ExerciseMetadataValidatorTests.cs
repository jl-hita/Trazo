using Trazo.Application.Catalog;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The rules every way into the catalog shares, the seed that must satisfy them and the report against the database.</summary>
public sealed class ExerciseMetadataValidatorTests
{
    private static ExerciseMetadata Valid() => new(
        "test-press", "Press", "Press", "Empuje.", ["Uno.", "Dos.", "Tres."], ["Consejo."], "push", "compound", "intermediate",
        "barbell", "chest", 5, false, false, "HorizontalPush", ["Chest"], ["Triceps"], "Beginner", ["Hypertrophy"],
        ["barbell", "bench"], 60);

    private static async Task<ExerciseSeed> SeedAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Trazo.Infrastructure", "Seeds"))) dir = dir.Parent;
        return await ExerciseSeed.LoadAsync(Path.Combine(dir!.FullName, "src", "Trazo.Infrastructure", "Seeds"));
    }

    [Fact]
    public void ACompleteExerciseIsValidAndGetsCanonicalSpelling()
    {
        var result = ExerciseMetadataValidator.Validate(Valid() with
        {
            MovementPattern = "horizontalpush", PrimaryMuscles = [" chest ", "Chest"], MinimumExperience = "beginner",
            Difficulty = "Intermediate", RequiredEquipment = ["Barbell", "bench"]
        });

        Assert.True(result.IsValid, result.Describe());
        Assert.Equal("HorizontalPush", result.Metadata.MovementPattern);
        Assert.Equal(["Chest"], result.Metadata.PrimaryMuscles);
        Assert.Equal("Beginner", result.Metadata.MinimumExperience);
        Assert.Equal("intermediate", result.Metadata.Difficulty);
        Assert.Equal(["barbell", "bench"], result.Metadata.RequiredEquipment);
    }

    public static TheoryData<string, Func<ExerciseMetadata, ExerciseMetadata>> Mistakes => new()
    {
        { "id", m => m with { Id = "Bad Id" } },
        { "nameEn", m => m with { NameEn = " " } },
        { "descriptionEs", m => m with { DescriptionEs = "" } },
        { "instructionsEs", m => m with { InstructionsEs = ["Uno.", "Dos."] } },
        { "movementPattern", m => m with { MovementPattern = "Push" } },
        { "movementPattern", m => m with { MovementPattern = "2" } },
        { "primaryMuscles", m => m with { PrimaryMuscles = [] } },
        { "primaryMuscles", m => m with { PrimaryMuscles = ["Pectoral"] } },
        { "secondaryMuscles", m => m with { SecondaryMuscles = ["Neck"] } },
        { "secondaryMuscles", m => m with { SecondaryMuscles = ["Chest"] } },
        { "suitableGoals", m => m with { SuitableGoals = [] } },
        { "suitableGoals", m => m with { SuitableGoals = ["Cutting"] } },
        { "minimumExperience", m => m with { MinimumExperience = "Expert" } },
        { "difficulty", m => m with { Difficulty = "hard" } },
        { "mechanic", m => m with { Mechanic = "unknown" } },
        { "forceType", m => m with { ForceType = "" } },
        { "bodyPart", m => m with { BodyPart = "arm" } },
        { "equipment", m => m with { Equipment = "Barbell and bench" } },
        { "requiredEquipment", m => m with { RequiredEquipment = ["machine"] } },
        { "requiredEquipment", m => m with { RequiredEquipment = ["flux-capacitor"] } },
        { "requiredEquipment", m => m with { RequiredEquipment = [] } },
        { "isUnilateral", m => m with { MovementPattern = "SingleLeg" } },
        { "met", m => m with { Met = 31 } },
        { "selectionPriority", m => m with { SelectionPriority = 101 } }
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void IncompleteOrUnknownMetadataIsRejectedOnItsField(string field, Func<ExerciseMetadata, ExerciseMetadata> mistake)
    {
        var result = ExerciseMetadataValidator.Validate(mistake(Valid()));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public async Task EverySeedExercisePassesTheSharedRules()
    {
        var problems = (await SeedAsync()).Problems();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>English is the only language still to be written: remove it from here when its file arrives.</summary>
    [Fact]
    public async Task TheSeedTranslatesEveryExerciseIntoEveryLanguageButEnglish()
    {
        var coverage = ExerciseSeedReportService.Coverage(await SeedAsync());

        Assert.Equal(["en"], coverage.Where(c => c.Incomplete > 0).Select(c => c.Language).ToArray());
        Assert.Equal(28, coverage.Count);
    }

    private static ExerciseCatalogItem Row(ExerciseSeedEntry e) => new()
    {
        Id = e.Id, Source = e.Source, NameEs = e.NameEs, NameEn = e.NameEn, DescriptionEs = e.DescriptionEs,
        InstructionsEs = e.InstructionsEs, TipsEs = e.TipsEs, ForceType = e.ForceType, Mechanic = e.Mechanic,
        Difficulty = e.Difficulty, Equipment = e.Equipment, BodyPart = e.BodyPart, Met = e.Met, IsUnilateral = e.IsUnilateral,
        IsBodyweight = e.IsBodyweight, MovementPattern = e.MovementPattern, PrimaryMuscles = e.PrimaryMuscles,
        SecondaryMuscles = e.SecondaryMuscles, MinimumExperience = e.MinimumExperience, SuitableGoals = e.SuitableGoals,
        RequiredEquipment = e.RequiredEquipment, SelectionPriority = e.SelectionPriority, IsActive = e.IsActive
    };

    private static List<ExerciseCatalogTranslation> Translations(ExerciseSeed seed) =>
        seed.Entries.Select(e => (e.Id, Language: "es", Text: new ExerciseText(e.NameEs, e.DescriptionEs, e.InstructionsEs, e.TipsEs)))
            .Concat(seed.Translations.SelectMany(l => l.Value.Select(t => (Id: t.Key, Language: l.Key, Text: t.Value))))
            .Select(t => new ExerciseCatalogTranslation
            {
                ExerciseId = t.Id, LanguageCode = t.Language, Name = t.Text.Name, Description = t.Text.Description,
                Instructions = t.Text.Instructions, Tips = t.Text.Tips, Status = "published"
            }).ToList();

    [Fact]
    public async Task TheReportIsCleanForADatabaseThatMatchesTheSeed()
    {
        var seed = await SeedAsync();

        var report = ExerciseSeedReportService.Compare(seed, seed.Entries.Select(Row).ToList(), Translations(seed));

        Assert.Empty(report.MissingInDatabase);
        Assert.Empty(report.Differences);
        Assert.Empty(report.OutsideSeed);
        Assert.Empty(report.Invalid);
    }

    [Fact]
    public async Task TheReportSeparatesPlanningDifferencesContentAndTranslations()
    {
        var seed = await SeedAsync();
        var rows = seed.Entries.Where(e => e.Id != "deadlift").Select(Row).ToList();
        var bench = rows.Single(r => r.Id == "bench-press");
        bench.MinimumExperience = "Intermediate";
        bench.NameEs = "Otro nombre";
        var own = Row(seed.Entries.Single(e => e.Id == "squat"));
        own.Id = "own-squat";
        own.Source = "trazo";
        own.PrimaryMuscles = ["Legs"];
        rows.Add(own);
        var translations = Translations(seed);
        translations.RemoveAll(t => t.ExerciseId == "bench-press" && t.LanguageCode == "de");
        translations.Single(t => t.ExerciseId == "bench-press" && t.LanguageCode == "fr").Name = "Autre";
        translations.Single(t => t.ExerciseId == "bench-press" && t.LanguageCode == "it").Status = "draft";

        var report = ExerciseSeedReportService.Compare(seed, rows, translations);

        Assert.Equal(["deadlift"], report.MissingInDatabase);
        var difference = Assert.Single(report.Differences);
        Assert.Equal("bench-press", difference.ExerciseId);
        Assert.Equal(["minimumExperience"], difference.EngineFields);
        Assert.Equal(["nameEs"], difference.ContentFields);
        Assert.Equal(["de:missing", "fr:different", "it:unpublished"], difference.Translations);
        Assert.Equal("own-squat", Assert.Single(report.OutsideSeed).ExerciseId);
        Assert.Contains("primaryMuscles", Assert.Single(report.Invalid).Errors.Keys);
    }
}
