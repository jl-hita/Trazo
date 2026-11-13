using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

public sealed record SkippedExport(string ExerciseId, string Reason);

public sealed record ExportResult(int ExerciseCount, int ImageCount, IReadOnlyList<SkippedExport> Skipped);

/// <summary>
/// Builds the archive TRAZO sends to DietoExpress (the reverse of the DietoExpress import).
/// Only exercises created in TRAZO are exported, together with the images uploaded or seeded in TRAZO;
/// exercises that came from DietoExpress are never sent back.
///
/// Archive layout (schemaVersion 1): <c>manifest.json</c>, <c>exercises.json</c> (the same entry format as
/// <c>exercise-catalog-additions.json</c>) and <c>images/{exerciseId}/{slot}.{ext}</c>.
/// </summary>
public sealed partial class ExerciseLibraryExporter(TrazoDbContext db)
{
    public const string SourceSystem = "TRAZO";
    private static readonly string[] OwnSources = ["trazo", "trazo-curated"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The receiving side only accepts lowercase letters, digits and hyphens in identifiers, and at least three steps.
    [GeneratedRegex("^[a-z0-9-]{1,100}$")]
    private static partial Regex ReceivingId();

    private sealed record ImageRow(string ExerciseId, int Slot, byte[] Data, string ContentType);

    public async Task<ExportResult> BuildAsync(Stream output, CancellationToken cancellationToken = default)
    {
        var items = await db.ExerciseCatalog.AsNoTracking()
            .Where(x => x.IsActive && OwnSources.Contains(x.Source))
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

        var skipped = new List<SkippedExport>();
        var entries = new List<object>();
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in items)
        {
            if (!ReceivingId().IsMatch(x.Id)) { skipped.Add(new(x.Id, "The identifier must use only lowercase letters, digits and hyphens.")); continue; }
            if (x.InstructionsEs.Length < 3) { skipped.Add(new(x.Id, "At least three instruction steps are required.")); continue; }
            accepted.Add(x.Id);
            entries.Add(new
            {
                id = x.Id,
                // Later edits get a higher version so the receiver can refresh its copy.
                version = (int)Math.Max(1, x.UpdatedAt.ToUnixTimeSeconds() / 60),
                nameEs = x.NameEs, nameEn = x.NameEn, descriptionEs = x.DescriptionEs,
                instructionsEs = x.InstructionsEs, tipsEs = x.TipsEs, forceType = x.ForceType, mechanic = x.Mechanic,
                difficulty = x.Difficulty, equipment = x.Equipment, bodyPart = x.BodyPart, met = x.Met,
                isUnilateral = x.IsUnilateral, isBodyweight = x.IsBodyweight, movementPattern = x.MovementPattern,
                primaryMuscles = x.PrimaryMuscles, secondaryMuscles = x.SecondaryMuscles,
                minimumExperience = x.MinimumExperience, suitableGoals = x.SuitableGoals,
                requiredEquipment = x.RequiredEquipment, selectionPriority = x.SelectionPriority
            });
        }

        var images = (await db.Database.SqlQuery<ImageRow>($"""
                SELECT exercise_id AS "ExerciseId", CAST(slot AS INTEGER) AS "Slot", data AS "Data", content_type AS "ContentType"
                FROM exercise_catalog_images WHERE origin_system = {SourceSystem} ORDER BY exercise_id, slot
                """).ToListAsync(cancellationToken))
            .Where(i => accepted.Contains(i.ExerciseId) && i.Data.Length <= ExerciseImageRules.MaximumBytes
                        && ExerciseImageRules.TryDetectContentType(i.Data, out _))
            .ToList();

        var exerciseBytes = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
        var imageManifest = images.Select(i =>
        {
            ExerciseImageRules.TryDetectContentType(i.Data, out var type);
            return new
            {
                exerciseId = i.ExerciseId, slot = i.Slot, path = $"images/{i.ExerciseId}/{i.Slot}.{ExerciseImageRules.Extension(type)}",
                contentType = type, source = "custom", size = i.Data.Length, sha256 = ExerciseImageRules.Sha256(i.Data)
            };
        }).ToList();

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteAsync(archive, "exercises.json", exerciseBytes, cancellationToken);
            foreach (var (image, meta) in images.Zip(imageManifest))
                await WriteAsync(archive, meta.path, image.Data, cancellationToken);
            var manifest = new
            {
                schemaVersion = 1, source = SourceSystem, exportedAtUtc = DateTimeOffset.UtcNow,
                exerciseCount = entries.Count, imageCount = imageManifest.Count,
                attribution = "Exercises created in TRAZO",
                exercisesSha256 = ExerciseImageRules.Sha256(exerciseBytes), images = imageManifest
            };
            await WriteAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);
        }
        return new ExportResult(entries.Count, imageManifest.Count, skipped);
    }

    private static async Task WriteAsync(ZipArchive archive, string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken);
    }
}
