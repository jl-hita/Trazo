using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Bootstraps the complete, reviewed exercise-library snapshot. Seed versions update only
/// untouched rows from the same source; administrator edits and rows from other sources win.
/// Safe to execute on every startup and serialized across concurrent API instances.
/// </summary>
public sealed class ExerciseCatalogSeeder(
    TrazoDbContext db,
    ILogger<ExerciseCatalogSeeder> logger)
{
    /// <summary>
    /// The translated catalog texts, one file per language (<c>Seeds/translations/{code}.json</c>, keyed by exercise id). Only missing rows
    /// are added: whatever the administrator has since edited stays.
    /// </summary>
    private async Task<int> SeedTranslationsAsync(ExerciseSeed seed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var exerciseIds = seed.Entries.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var (language, entries) in seed.Translations)
        {
            var present = (await db.ExerciseCatalogTranslations.Where(x => x.LanguageCode == language)
                .Select(x => x.ExerciseId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            foreach (var (id, text) in entries.Where(e => exerciseIds.Contains(e.Key) && !present.Contains(e.Key)))
            {
                db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
                {
                    ExerciseId = id, LanguageCode = language, Name = text.Name, Description = text.Description,
                    Instructions = text.Instructions, Tips = text.Tips, Status = "published", UpdatedAt = now
                });
                added++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seed = await ExerciseSeed.LoadAsync(ExerciseSeed.DefaultFolder, cancellationToken);
        var problems = seed.Problems();
        if (problems.Count > 0)
            throw new InvalidDataException("Exercise catalog seed failed validation. " + string.Join(" ", problems));
        var entries = seed.Entries;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731904221)", cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var updated = 0;
        var preserved = 0;
        var translationsAdded = 0;

        foreach (var entry in entries)
        {
            var existing = await db.ExerciseCatalog.SingleOrDefaultAsync(x => x.Id == entry.Id, cancellationToken);
            if (existing is null)
            {
                db.ExerciseCatalog.Add(new ExerciseCatalogItem
                {
                    Id = entry.Id,
                    Source = entry.Source,
                    NameEn = entry.NameEn,
                    NameEs = entry.NameEs,
                    DescriptionEs = entry.DescriptionEs,
                    InstructionsEs = entry.InstructionsEs,
                    TipsEs = entry.TipsEs,
                    ForceType = entry.ForceType,
                    Mechanic = entry.Mechanic,
                    Difficulty = entry.Difficulty,
                    Equipment = entry.Equipment,
                    BodyPart = entry.BodyPart,
                    Met = entry.Met,
                    IsUnilateral = entry.IsUnilateral,
                    IsBodyweight = entry.IsBodyweight,
                    MovementPattern = entry.MovementPattern,
                    PrimaryMuscles = entry.PrimaryMuscles,
                    SecondaryMuscles = entry.SecondaryMuscles,
                    MinimumExperience = entry.MinimumExperience,
                    SuitableGoals = entry.SuitableGoals,
                    RequiredEquipment = ExerciseEquipmentRequirements.Normalize(entry.Id, entry.RequiredEquipment),
                    SelectionPriority = entry.SelectionPriority,
                    SeedVersion = entry.Version,
                    IsActive = entry.IsActive,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                inserted++;

                db.ExerciseCatalogTranslations.AddRange(
                    new ExerciseCatalogTranslation
                    {
                        ExerciseId = entry.Id,
                        LanguageCode = "es",
                        Name = entry.NameEs,
                        Description = entry.DescriptionEs,
                        Instructions = entry.InstructionsEs,
                        Tips = entry.TipsEs,
                        Status = "published",
                        UpdatedAt = now
                    },
                    new ExerciseCatalogTranslation
                    {
                        ExerciseId = entry.Id,
                        LanguageCode = "en",
                        Name = entry.NameEn,
                        Status = "draft",
                        UpdatedAt = now
                    });
                translationsAdded += 2;
                continue;
            }

            if (existing.Source == entry.Source &&
                existing.SeedVersion < entry.Version &&
                existing.UpdatedAt == existing.CreatedAt)
            {
                existing.NameEn = entry.NameEn;
                existing.NameEs = entry.NameEs;
                existing.DescriptionEs = entry.DescriptionEs;
                existing.InstructionsEs = entry.InstructionsEs;
                existing.TipsEs = entry.TipsEs;
                existing.ForceType = entry.ForceType;
                existing.Mechanic = entry.Mechanic;
                existing.Difficulty = entry.Difficulty;
                existing.Equipment = entry.Equipment;
                existing.BodyPart = entry.BodyPart;
                existing.Met = entry.Met;
                existing.IsUnilateral = entry.IsUnilateral;
                existing.IsBodyweight = entry.IsBodyweight;
                existing.MovementPattern = entry.MovementPattern;
                existing.PrimaryMuscles = entry.PrimaryMuscles;
                existing.SecondaryMuscles = entry.SecondaryMuscles;
                existing.MinimumExperience = entry.MinimumExperience;
                existing.SuitableGoals = entry.SuitableGoals;
                existing.RequiredEquipment = ExerciseEquipmentRequirements.Normalize(entry.Id, entry.RequiredEquipment);
                existing.SelectionPriority = entry.SelectionPriority;
                existing.IsActive = entry.IsActive;
                existing.SeedVersion = entry.Version;
                existing.UpdatedAt = now;
                updated++;
            }
            else
            {
                preserved++;
            }

            // Repair missing language rows without replacing translations already curated in the admin.
            var languages = await db.ExerciseCatalogTranslations
                .Where(x => x.ExerciseId == entry.Id)
                .Select(x => x.LanguageCode)
                .ToListAsync(cancellationToken);
            if (!languages.Contains("es", StringComparer.Ordinal))
            {
                db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
                {
                    ExerciseId = entry.Id, LanguageCode = "es", Name = entry.NameEs,
                    Description = entry.DescriptionEs, Instructions = entry.InstructionsEs,
                    Tips = entry.TipsEs, Status = "published", UpdatedAt = now
                });
                translationsAdded++;
            }
            if (!languages.Contains("en", StringComparer.Ordinal))
            {
                db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
                {
                    ExerciseId = entry.Id, LanguageCode = "en", Name = entry.NameEn,
                    Status = "draft", UpdatedAt = now
                });
                translationsAdded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        translationsAdded += await SeedTranslationsAsync(seed, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Complete exercise catalog seed processed: {SeedEntries} entries, {Inserted} inserted, {Updated} untouched rows refreshed, {Preserved} existing rows preserved, {TranslationsAdded} missing translations added.",
            entries.Count, inserted, updated, preserved, translationsAdded);
    }
}
