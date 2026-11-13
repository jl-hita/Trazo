using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Loads first-party exercise illustrations shipped with the API (<c>Seeds/exercise-images/{exerciseId}/{slot}.webp</c>).
/// Only missing images are inserted, so an image replaced from the administration console is never overwritten.
/// The seed owns the rows it created (source <see cref="Source"/>): when a file is removed from the seed, the row it
/// created is removed from the database too. Uploaded images and images imported from other systems are never touched.
/// </summary>
public sealed class ExerciseImageSeeder(TrazoDbContext db, ILogger<ExerciseImageSeeder> logger)
{
    public const string Source = "trazo-curated";
    public const string OriginSystem = "TRAZO";

    public async Task SeedAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        var root = directory ?? Path.Combine(AppContext.BaseDirectory, "Seeds", "exercise-images");
        var shipped = new HashSet<(string ExerciseId, int Slot)>();
        if (!Directory.Exists(root))
        {
            logger.LogInformation("No exercise image seed directory; nothing to load.");
            await RemoveWithdrawnAsync(shipped, cancellationToken);
            return;
        }

        var knownIds = (await db.ExerciseCatalog.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();
        int inserted = 0, skipped = 0;
        foreach (var exerciseDirectory in Directory.EnumerateDirectories(root).Order())
        {
            var exerciseId = Path.GetFileName(exerciseDirectory);
            if (!knownIds.Contains(exerciseId))
            {
                logger.LogWarning("Image seed for unknown exercise '{ExerciseId}' ignored.", exerciseId);
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(exerciseDirectory).Order())
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var slot) || slot is < 0 or > ExerciseImageRules.MaximumSlot)
                    throw new InvalidDataException($"Image seed '{file}' must be named after a slot from 0 to {ExerciseImageRules.MaximumSlot}.");
                shipped.Add((exerciseId, slot));
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                if (bytes.Length > ExerciseImageRules.MaximumBytes ||
                    !ExerciseImageRules.TryDetectContentType(bytes, out var contentType))
                    throw new InvalidDataException($"Image seed '{file}' is not a valid image of at most {ExerciseImageRules.MaximumBytes} bytes.");

                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO exercise_catalog_images (exercise_id, slot, content_type, data, sha256, source, origin_system, imported_at)
                    VALUES ({exerciseId}, {slot}, {contentType}, {bytes}, {ExerciseImageRules.Sha256(bytes)}, {Source}, {OriginSystem}, {DateTimeOffset.UtcNow})
                    ON CONFLICT (exercise_id, slot) DO NOTHING
                    """, cancellationToken);
                if (changed > 0) inserted++; else skipped++;
            }
        }
        var removed = await RemoveWithdrawnAsync(shipped, cancellationToken);
        logger.LogInformation("Exercise image seed processed: {Inserted} inserted, {Existing} already present, {Removed} withdrawn.", inserted, skipped, removed);
    }

    /// <summary>Removes the images this seed once created that it no longer ships.</summary>
    private async Task<int> RemoveWithdrawnAsync(HashSet<(string ExerciseId, int Slot)> shipped, CancellationToken cancellationToken)
    {
        var seeded = await db.Database
            .SqlQuery<SeededImage>($"SELECT exercise_id AS \"ExerciseId\", CAST(slot AS INTEGER) AS \"Slot\" FROM exercise_catalog_images WHERE source = {Source} AND origin_system = {OriginSystem}")
            .ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var image in seeded.Where(x => !shipped.Contains((x.ExerciseId, x.Slot))))
            removed += await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM exercise_catalog_images
                WHERE exercise_id = {image.ExerciseId} AND slot = {image.Slot} AND source = {Source} AND origin_system = {OriginSystem}
                """, cancellationToken);
        return removed;
    }

    private sealed record SeededImage(string ExerciseId, int Slot);
}
