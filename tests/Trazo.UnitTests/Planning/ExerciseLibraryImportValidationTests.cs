using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The DietoExpress import applies the shared exercise rules before it touches the database.</summary>
public sealed class ExerciseLibraryImportValidationTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _archive = Path.Combine(Path.GetTempPath(), $"trazo-import-{Guid.NewGuid():N}.zip");

    public void Dispose()
    {
        _fixture.Dispose();
        File.Delete(_archive);
    }

    [Fact]
    public async Task AnArchiveWithInvalidMetadataWritesNothingAndSaysWhatIsWrong()
    {
        var exercises = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                id = "odd-press", source = "dietoexpress-curated", reviewStatus = "approved", reviewed = true,
                nameEs = "Press", nameEn = "Press", descriptionEs = "Empuje.", instructionsEs = new[] { "Uno.", "Dos.", "Tres." },
                forceType = "push", mechanic = "compound", difficulty = "intermediate", bodyPart = "chest",
                movementPattern = "HorizontalPush", primaryMuscles = new[] { "Pectoral" }, minimumExperience = "Beginner",
                suitableGoals = new[] { "Hypertrophy" }, requiredEquipment = new[] { "barbell" }
            },
            // A seed exercise is not imported, so what the archive says about it is not even judged.
            new
            {
                id = "bench-press", source = "dietoexpress-curated", reviewStatus = "approved", reviewed = true,
                nameEs = "", nameEn = "", descriptionEs = "", instructionsEs = Array.Empty<string>(),
                forceType = "", mechanic = "", difficulty = "", bodyPart = "",
                movementPattern = "", primaryMuscles = Array.Empty<string>(), minimumExperience = "",
                suitableGoals = Array.Empty<string>(), requiredEquipment = Array.Empty<string>()
            }
        });
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, source = "DietoExpress", exportedAtUtc = DateTimeOffset.UtcNow, exerciseCount = 2, imageCount = 0,
            attribution = "test", exercisesSha256 = Convert.ToHexString(SHA256.HashData(exercises)).ToLowerInvariant(),
            images = Array.Empty<object>()
        });
        using (var zip = ZipFile.Open(_archive, ZipArchiveMode.Create))
        {
            await using (var stream = zip.CreateEntry("manifest.json").Open()) await stream.WriteAsync(Encoding.UTF8.GetBytes(manifest));
            await using (var stream = zip.CreateEntry("exercises.json").Open()) await stream.WriteAsync(exercises);
        }

        using var db = _fixture.NewContext();
        var service = new ExerciseLibraryImportService(db, new ExerciseSeedIndex(), NullLogger<ExerciseLibraryImportService>.Instance);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(_archive));

        Assert.Contains("odd-press", error.Message);
        Assert.Contains("primaryMuscles", error.Message);
        Assert.Contains("1 exercise(s)", error.Message);
        Assert.DoesNotContain("bench-press", error.Message);
    }
}
