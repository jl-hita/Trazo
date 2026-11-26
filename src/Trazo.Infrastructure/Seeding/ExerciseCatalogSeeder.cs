using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Application.Catalog;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Writes the exercise seed into the database. The seed is the baseline of the catalog and wins: every exercise it holds gets
/// its metadata, visibility and texts from it, whatever was edited before, so those exercises are changed by changing the seed.
/// Exercises and translation languages the seed does not have are left alone. Only rows that differ are written, so it is safe
/// on every startup, and it is serialized across concurrent API instances.
/// </summary>
public sealed class ExerciseCatalogSeeder(
    TrazoDbContext db,
    ILogger<ExerciseCatalogSeeder> logger)
{
    private int translationsAdded;
    private int translationsUpdated;

    public async Task SeedAsync(CancellationToken cancellationToken = default, string? folder = null)
    {
        var seed = await ExerciseSeed.LoadAsync(folder ?? ExerciseSeed.DefaultFolder, cancellationToken);
        var problems = seed.Problems();
        if (problems.Count > 0)
            throw new InvalidDataException("Exercise catalog seed failed validation. " + string.Join(" ", problems));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731904221)", cancellationToken);

        var ids = seed.Entries.Select(e => e.Id).ToList();
        var rows = await db.ExerciseCatalog.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, StringComparer.Ordinal, cancellationToken);
        var translations = (await db.ExerciseCatalogTranslations.Where(x => ids.Contains(x.ExerciseId)).ToListAsync(cancellationToken))
            .ToDictionary(x => (x.ExerciseId, x.LanguageCode));
        translationsAdded = translationsUpdated = 0;

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var updated = 0;
        foreach (var entry in seed.Entries)
        {
            var metadata = ExerciseMetadataValidator.Validate(entry.Metadata()).Metadata;
            if (!rows.TryGetValue(entry.Id, out var row))
            {
                row = new ExerciseCatalogItem
                {
                    Id = entry.Id, Source = entry.Source, NameEn = metadata.NameEn, NameEs = metadata.NameEs,
                    DescriptionEs = metadata.DescriptionEs, ForceType = metadata.ForceType, Mechanic = metadata.Mechanic,
                    Difficulty = metadata.Difficulty, BodyPart = metadata.BodyPart, MovementPattern = metadata.MovementPattern,
                    MinimumExperience = metadata.MinimumExperience, CreatedAt = now, UpdatedAt = now
                };
                Apply(row, entry, metadata);
                db.ExerciseCatalog.Add(row);
                inserted++;
            }
            else if (!Matches(row, entry, metadata))
            {
                Apply(row, entry, metadata);
                row.UpdatedAt = now;
                updated++;
            }

            Write(translations, entry.Id, "es", new(metadata.NameEs, metadata.DescriptionEs, metadata.InstructionsEs, metadata.TipsEs), now);
            foreach (var (language, texts) in seed.Translations)
                if (texts.TryGetValue(entry.Id, out var text)) Write(translations, entry.Id, language, text, now);

            // Until the seed has its own English file, it only owns the English name: the other English texts stay as they are.
            if (!seed.Translations.ContainsKey("en")) WriteEnglishName(translations, entry.Id, metadata.NameEn, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Exercise catalog seed applied: {SeedEntries} exercises, {Inserted} inserted, {Updated} brought back to the seed; {TranslationsAdded} translations added, {TranslationsUpdated} updated.",
            seed.Entries.Count, inserted, updated, translationsAdded, translationsUpdated);
    }

    private static bool Matches(ExerciseCatalogItem row, ExerciseSeedEntry entry, ExerciseMetadata m) =>
        row.Source == entry.Source && row.SeedVersion == entry.Version && row.IsActive == entry.IsActive &&
        row.NameEn == m.NameEn && row.NameEs == m.NameEs && row.DescriptionEs == m.DescriptionEs &&
        row.InstructionsEs.SequenceEqual(m.InstructionsEs) && row.TipsEs.SequenceEqual(m.TipsEs) &&
        row.ForceType == m.ForceType && row.Mechanic == m.Mechanic && row.Difficulty == m.Difficulty &&
        row.Equipment == m.Equipment && row.BodyPart == m.BodyPart && row.Met.Equals(m.Met) &&
        row.IsUnilateral == m.IsUnilateral && row.IsBodyweight == m.IsBodyweight && row.MovementPattern == m.MovementPattern &&
        row.PrimaryMuscles.SequenceEqual(m.PrimaryMuscles) && row.SecondaryMuscles.SequenceEqual(m.SecondaryMuscles) &&
        row.MinimumExperience == m.MinimumExperience && row.SuitableGoals.SequenceEqual(m.SuitableGoals) &&
        row.RequiredEquipment.SequenceEqual(m.RequiredEquipment) && row.SelectionPriority == m.SelectionPriority;

    private static void Apply(ExerciseCatalogItem row, ExerciseSeedEntry entry, ExerciseMetadata m)
    {
        row.Source = entry.Source; row.SeedVersion = entry.Version; row.IsActive = entry.IsActive;
        row.NameEn = m.NameEn; row.NameEs = m.NameEs; row.DescriptionEs = m.DescriptionEs;
        row.InstructionsEs = m.InstructionsEs; row.TipsEs = m.TipsEs; row.ForceType = m.ForceType; row.Mechanic = m.Mechanic;
        row.Difficulty = m.Difficulty; row.Equipment = m.Equipment; row.BodyPart = m.BodyPart; row.Met = m.Met;
        row.IsUnilateral = m.IsUnilateral; row.IsBodyweight = m.IsBodyweight; row.MovementPattern = m.MovementPattern;
        row.PrimaryMuscles = m.PrimaryMuscles; row.SecondaryMuscles = m.SecondaryMuscles;
        row.MinimumExperience = m.MinimumExperience; row.SuitableGoals = m.SuitableGoals;
        row.RequiredEquipment = m.RequiredEquipment; row.SelectionPriority = m.SelectionPriority;
    }

    private void Write(Dictionary<(string, string), ExerciseCatalogTranslation> stored, string id, string language, ExerciseText text, DateTimeOffset now)
    {
        if (!stored.TryGetValue((id, language), out var row))
        {
            db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
            {
                ExerciseId = id, LanguageCode = language, Name = text.Name, Description = text.Description,
                Instructions = text.Instructions, Tips = text.Tips, Status = "published", UpdatedAt = now
            });
            translationsAdded++;
            return;
        }
        if (row.Name == text.Name && row.Description == text.Description && row.Status == "published" &&
            (row.Instructions ?? []).SequenceEqual(text.Instructions) && (row.Tips ?? []).SequenceEqual(text.Tips)) return;
        row.Name = text.Name; row.Description = text.Description; row.Instructions = text.Instructions; row.Tips = text.Tips;
        row.Status = "published"; row.UpdatedAt = now;
        translationsUpdated++;
    }

    private void WriteEnglishName(Dictionary<(string, string), ExerciseCatalogTranslation> stored, string id, string name, DateTimeOffset now)
    {
        if (!stored.TryGetValue((id, "en"), out var row))
        {
            db.ExerciseCatalogTranslations.Add(new ExerciseCatalogTranslation
            {
                ExerciseId = id, LanguageCode = "en", Name = name, Status = "draft", UpdatedAt = now
            });
            translationsAdded++;
        }
        else if (row.Name != name)
        {
            row.Name = name;
            row.UpdatedAt = now;
            translationsUpdated++;
        }
    }
}
