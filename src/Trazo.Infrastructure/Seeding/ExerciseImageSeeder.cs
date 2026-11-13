using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Loads first-party exercise illustrations shipped with the API (<c>Seeds/exercise-images/{exerciseId}/{slot}.webp</c>).
/// Only missing images are inserted, so an image replaced from the administration console is never overwritten.
/// </summary>
public sealed class ExerciseImageSeeder(TrazoDbContext db, ILogger<ExerciseImageSeeder> logger)
{
    public const string Source = "trazo-curated";
    public const string OriginSystem = "TRAZO";

    public async Task SeedAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        var root = directory ?? Path.Combine(AppContext.BaseDirectory, "Seeds", "exercise-images");
        if (!Directory.Exists(root))
        {
            logger.LogInformation("No exercise image seed directory; nothing to load.");
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
        logger.LogInformation("Exercise image seed processed: {Inserted} inserted, {Existing} already present.", inserted, skipped);
    }
}
