using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Writes a complete snapshot of the database-backed exercise catalog for offline
/// inspection. The snapshot is generated after migrations and all catalog seeders run.
/// </summary>
public sealed class ExerciseCatalogArtifactExporter(
    TrazoDbContext db,
    IConfiguration configuration,
    ILogger<ExerciseCatalogArtifactExporter> logger)
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record Exercise(
        string Id,
        string Source,
        string NameEn,
        string NameEs,
        string DescriptionEs,
        string[] InstructionsEs,
        string[] TipsEs,
        string ForceType,
        string Mechanic,
        string Difficulty,
        string? Equipment,
        string BodyPart,
        double Met,
        bool IsUnilateral,
        bool IsBodyweight,
        string MovementPattern,
        string[] PrimaryMuscles,
        string[] SecondaryMuscles,
        string MinimumExperience,
        string[] SuitableGoals,
        string[] RequiredEquipment,
        int SelectionPriority,
        int SeedVersion,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string[] EffectiveSecondaryMuscles,
        string[] Emphasis,
        string[] LimitationAreas,
        bool MetadataReviewed);

    private sealed record Artifact(
        int SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        int TotalCount,
        int ActiveCount,
        IReadOnlyList<Exercise> Exercises);

    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        var exercises = await db.ExerciseCatalog
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new Exercise(
                x.Id,
                x.Source,
                x.NameEn,
                x.NameEs,
                x.DescriptionEs,
                x.InstructionsEs,
                x.TipsEs,
                x.ForceType,
                x.Mechanic,
                x.Difficulty,
                x.Equipment,
                x.BodyPart,
                x.Met,
                x.IsUnilateral,
                x.IsBodyweight,
                x.MovementPattern,
                x.PrimaryMuscles,
                x.SecondaryMuscles,
                x.MinimumExperience,
                x.SuitableGoals,
                x.RequiredEquipment,
                x.SelectionPriority,
                x.SeedVersion,
                x.IsActive,
                x.CreatedAt,
                x.UpdatedAt,
                x.EffectiveSecondaryMuscles,
                x.Emphasis,
                x.LimitationAreas,
                x.MetadataReviewed))
            .ToListAsync(cancellationToken);

        var artifact = new Artifact(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            exercises.Count,
            exercises.Count(x => x.IsActive),
            exercises);

        var configuredPath = configuration["ExerciseCatalogArtifact:Path"];
        var outputPath = string.IsNullOrWhiteSpace(configuredPath)
            ? "/var/lib/trazo/artifacts/exercise-catalog.json"
            : configuredPath.Trim();

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The exercise catalog artifact path must include a directory.");

        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsLinux())
        {
            // The catalog contains exercise metadata only. Make this one directory
            // readable by the self-hosted Actions runner without exposing the
            // private Data Protection directory or allowing directory enumeration
            // of /var/lib/trazo itself.
            if (string.Equals(fullPath, "/var/lib/trazo/artifacts/exercise-catalog.json", StringComparison.Ordinal))
            {
                File.SetUnixFileMode(
                    "/var/lib/trazo",
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var temporaryPath = Path.Combine(directory, $".exercise-catalog-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, artifact, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            // Keep the previous complete snapshot intact if serialization or writing fails.
            File.Move(temporaryPath, fullPath, overwrite: true);

            logger.LogInformation(
                "Exercise catalog artifact generated at {ArtifactPath}: {TotalCount} exercises ({ActiveCount} active).",
                fullPath,
                artifact.TotalCount,
                artifact.ActiveCount);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
