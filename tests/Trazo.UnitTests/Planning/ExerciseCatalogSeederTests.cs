using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The seed is the baseline of the catalog: it wins over the database for the exercises it holds.</summary>
public sealed class ExerciseCatalogSeederTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _seed = SeedFolder();

    public ExerciseCatalogSeederTests()
    {
        using var db = _fixture.NewContext();
        ExerciseTestSchema.Create(db);
    }

    public void Dispose() => _fixture.Dispose();

    private static string SeedFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Trazo.Infrastructure", "Seeds"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Trazo.Infrastructure", "Seeds");
    }

    private async Task SeedAsync()
    {
        using var db = _fixture.NewContext();
        await new ExerciseCatalogSeeder(db, NullLogger<ExerciseCatalogSeeder>.Instance).SeedAsync(folder: _seed);
    }

    private async Task<ExerciseSeedReport> ReportAsync()
    {
        using var db = _fixture.NewContext();
        return ExerciseSeedReportService.Compare(await ExerciseSeed.LoadAsync(_seed),
            await db.ExerciseCatalog.AsNoTracking().ToListAsync(), await db.ExerciseCatalogTranslations.AsNoTracking().ToListAsync());
    }

    private static void AssertMatchesTheSeed(ExerciseSeedReport report)
    {
        Assert.Empty(report.MissingInDatabase);
        Assert.Empty(report.Differences);
    }

    [Fact]
    public async Task AFreshInstallationHoldsExactlyTheSeed_AndASecondStartWritesNothing()
    {
        await SeedAsync();
        AssertMatchesTheSeed(await ReportAsync());
        Dictionary<string, DateTimeOffset> exercises;
        List<(string, string, DateTimeOffset)> texts;
        using (var db = _fixture.NewContext())
        {
            exercises = await db.ExerciseCatalog.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.UpdatedAt);
            texts = (await db.ExerciseCatalogTranslations.AsNoTracking().ToListAsync())
                .Select(t => (t.ExerciseId, t.LanguageCode, t.UpdatedAt)).Order().ToList();
        }
        Assert.Equal((await ExerciseSeed.LoadAsync(_seed)).Entries.Count, exercises.Count);

        await SeedAsync();

        using var after = _fixture.NewContext();
        Assert.Equal(exercises, await after.ExerciseCatalog.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.UpdatedAt));
        Assert.Equal(texts, (await after.ExerciseCatalogTranslations.AsNoTracking().ToListAsync())
            .Select(t => (t.ExerciseId, t.LanguageCode, t.UpdatedAt)).Order().ToList());
    }

    [Fact]
    public async Task EditedSeedExercisesComeBackToTheSeed_WithTheirTranslations()
    {
        await SeedAsync();
        DateTimeOffset created;
        using (var db = _fixture.NewContext())
        {
            var bench = await db.ExerciseCatalog.SingleAsync(x => x.Id == "bench-press");
            created = bench.CreatedAt;
            bench.MinimumExperience = "Intermediate";
            bench.NameEs = "Editado a mano";
            bench.Source = "dietoexpress-curated";
            bench.IsActive = false;
            bench.PrimaryMuscles = ["Triceps"];
            bench.UpdatedAt = bench.CreatedAt.AddDays(1);
            var german = await db.ExerciseCatalogTranslations.SingleAsync(x => x.ExerciseId == "bench-press" && x.LanguageCode == "de");
            german.Name = "Von Hand";
            german.Status = "draft";
            db.ExerciseCatalogTranslations.Remove(
                await db.ExerciseCatalogTranslations.SingleAsync(x => x.ExerciseId == "bench-press" && x.LanguageCode == "fr"));
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        AssertMatchesTheSeed(await ReportAsync());
        using var after = _fixture.NewContext();
        var restored = await after.ExerciseCatalog.AsNoTracking().SingleAsync(x => x.Id == "bench-press");
        Assert.Equal("Beginner", restored.MinimumExperience);
        Assert.True(restored.IsActive);
        Assert.Equal(created, restored.CreatedAt);
        Assert.Equal("published", (await after.ExerciseCatalogTranslations.AsNoTracking()
            .SingleAsync(x => x.ExerciseId == "bench-press" && x.LanguageCode == "de")).Status);
    }

    [Fact]
    public async Task WhatTheSeedDoesNotHaveIsLeftAlone()
    {
        await SeedAsync();
        using (var db = _fixture.NewContext())
        {
            var own = ExerciseCandidateMapperTests.Item("own-press");
            own.Source = "trazo";
            own.NameEs = "Propio";
            db.ExerciseCatalog.Add(own);
            db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
            {
                ExerciseId = "own-press", LanguageCode = "de", Name = "Eigene", Status = "draft"
            });
            // A language the seed does not have.
            db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
            {
                ExerciseId = "bench-press", LanguageCode = "xx", Name = "Other language", Status = "published"
            });
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        using var after = _fixture.NewContext();
        Assert.Equal("Propio", (await after.ExerciseCatalog.AsNoTracking().SingleAsync(x => x.Id == "own-press")).NameEs);
        Assert.Equal("Eigene", (await after.ExerciseCatalogTranslations.AsNoTracking().SingleAsync(x => x.ExerciseId == "own-press")).Name);
        Assert.Equal("Other language", (await after.ExerciseCatalogTranslations.AsNoTracking()
            .SingleAsync(x => x.ExerciseId == "bench-press" && x.LanguageCode == "xx")).Name);
    }
}
