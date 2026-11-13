using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class ExerciseImageSeederTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trazo-images-{Guid.NewGuid():N}");

    public ExerciseImageSeederTests()
    {
        using var db = _fixture.NewContext();
        ExerciseTestSchema.Create(db);
        db.ExerciseCatalog.Add(ExerciseCandidateMapperTests.Item("seeded-press"));
        db.SaveChanges();
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try { Directory.Delete(_directory, true); } catch (IOException) { }
    }

    private static byte[] Webp(byte fill)
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes); "WEBP"u8.CopyTo(bytes.AsSpan(8));
        Array.Fill(bytes, fill, 12, 28);
        return bytes;
    }

    private void Put(string exerciseId, string file, byte[] data)
    {
        var folder = Path.Combine(_directory, exerciseId);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), data);
    }

    private async Task SeedAsync()
    {
        await using var db = _fixture.NewContext();
        await new ExerciseImageSeeder(db, NullLogger<ExerciseImageSeeder>.Instance).SeedAsync(_directory);
    }

    private async Task<int> CountAsync()
    {
        await using var db = _fixture.NewContext();
        return await db.Database.SqlQuery<int>($"SELECT CAST(COUNT(*) AS INTEGER) AS \"Value\" FROM exercise_catalog_images").SingleAsync();
    }

    [Fact]
    public async Task InsertsMissingImages_AndNeverOverwritesExistingOnes()
    {
        Put("seeded-press", "0.webp", Webp(1));
        Put("seeded-press", "1.webp", Webp(2));
        await SeedAsync();
        Assert.Equal(2, await CountAsync());

        await using (var db = _fixture.NewContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE exercise_catalog_images SET source = 'trazo-upload', sha256 = 'edited' WHERE slot = 0");
        Put("seeded-press", "0.webp", Webp(7)); // a changed seed must not replace the edited image
        await SeedAsync();

        await using var check = _fixture.NewContext();
        Assert.Equal(2, await CountAsync());
        Assert.Equal("edited", await check.Database.SqlQuery<string>($"SELECT sha256 AS \"Value\" FROM exercise_catalog_images WHERE slot = 0").SingleAsync());
    }

    [Fact]
    public async Task IgnoresUnknownExercises_AndRejectsInvalidFiles()
    {
        Put("not-in-catalog", "0.webp", Webp(1));
        await SeedAsync();
        Assert.Equal(0, await CountAsync());

        Put("seeded-press", "front.webp", Webp(1));
        await Assert.ThrowsAsync<InvalidDataException>(SeedAsync);

        File.Delete(Path.Combine(_directory, "seeded-press", "front.webp"));
        Put("seeded-press", "2.webp", "definitely not an image"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(SeedAsync);
    }
}
