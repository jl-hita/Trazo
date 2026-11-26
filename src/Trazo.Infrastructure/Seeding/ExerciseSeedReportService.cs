using Microsoft.EntityFrameworkCore;
using Trazo.Application.Catalog;
using Trazo.Application.Localization;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// How one seed exercise differs in the database. <c>EngineFields</c> change the plans, <c>ContentFields</c> only what is shown;
/// <c>Translations</c> lists <c>language:missing|different|unpublished</c>.
/// </summary>
public sealed record SeedDifference(
    string ExerciseId, IReadOnlyList<string> EngineFields, IReadOnlyList<string> ContentFields, IReadOnlyList<string> Translations);

public sealed record InvalidExercise(string ExerciseId, string Source, bool IsActive, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>An exercise of the database that the seed does not have (created in the console or imported).</summary>
public sealed record ExerciseOutsideSeed(string ExerciseId, string Source, bool IsActive);

/// <summary>How many seed exercises the seed itself translates completely into one language.</summary>
public sealed record SeedLanguageCoverage(string Language, int Complete, int Incomplete);

public sealed record ExerciseSeedReport(
    int SeedExercises, int DatabaseExercises, IReadOnlyList<string> MissingInDatabase, IReadOnlyList<SeedDifference> Differences,
    IReadOnlyList<ExerciseOutsideSeed> OutsideSeed, IReadOnlyList<InvalidExercise> Invalid, IReadOnlyList<SeedLanguageCoverage> Languages);

/// <summary>Compares the exercise catalog of the database with the seed shipped with the application. It never writes.</summary>
public sealed class ExerciseSeedReportService(TrazoDbContext db)
{
    public async Task<ExerciseSeedReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var seed = await ExerciseSeed.LoadAsync(ExerciseSeed.DefaultFolder, cancellationToken);
        var rows = await db.ExerciseCatalog.AsNoTracking().ToListAsync(cancellationToken);
        var translations = await db.ExerciseCatalogTranslations.AsNoTracking().ToListAsync(cancellationToken);
        return Compare(seed, rows, translations);
    }

    public static ExerciseSeedReport Compare(
        ExerciseSeed seed, IReadOnlyList<ExerciseCatalogItem> rows, IReadOnlyList<ExerciseCatalogTranslation> translations)
    {
        var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var stored = translations.ToDictionary(t => (t.ExerciseId, t.LanguageCode));
        var seedIds = seed.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        var differences = new List<SeedDifference>();
        foreach (var entry in seed.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(entry.Id, out var row)) { missing.Add(entry.Id); continue; }
            var expected = entry.Metadata();
            var engine = new List<string>();
            Check(engine, "movementPattern", expected.MovementPattern == row.MovementPattern);
            Check(engine, "primaryMuscles", SameSet(expected.PrimaryMuscles, row.PrimaryMuscles));
            Check(engine, "secondaryMuscles", SameSet(expected.SecondaryMuscles, row.SecondaryMuscles));
            Check(engine, "requiredEquipment", SameSet(expected.RequiredEquipment, row.RequiredEquipment));
            Check(engine, "minimumExperience", expected.MinimumExperience == row.MinimumExperience);
            Check(engine, "suitableGoals", SameSet(expected.SuitableGoals, row.SuitableGoals));
            Check(engine, "selectionPriority", expected.SelectionPriority == row.SelectionPriority);
            Check(engine, "isActive", entry.IsActive == row.IsActive);

            var content = new List<string>();
            Check(content, "source", entry.Source == row.Source);
            Check(content, "nameEs", expected.NameEs == row.NameEs);
            Check(content, "nameEn", expected.NameEn == row.NameEn);
            Check(content, "descriptionEs", expected.DescriptionEs == row.DescriptionEs);
            Check(content, "instructionsEs", expected.InstructionsEs.SequenceEqual(row.InstructionsEs));
            Check(content, "tipsEs", expected.TipsEs.SequenceEqual(row.TipsEs));
            Check(content, "forceType", expected.ForceType == row.ForceType);
            Check(content, "mechanic", expected.Mechanic == row.Mechanic);
            Check(content, "difficulty", expected.Difficulty == row.Difficulty);
            Check(content, "equipment", expected.Equipment == row.Equipment);
            Check(content, "bodyPart", expected.BodyPart == row.BodyPart);
            Check(content, "met", expected.Met.Equals(row.Met));
            Check(content, "isUnilateral", expected.IsUnilateral == row.IsUnilateral);
            Check(content, "isBodyweight", expected.IsBodyweight == row.IsBodyweight);

            var texts = new List<string>();
            foreach (var (language, text) in SeedTexts(seed, entry))
            {
                if (!stored.TryGetValue((entry.Id, language), out var translation)) texts.Add($"{language}:missing");
                else if (translation.Name != text.Name || translation.Description != text.Description ||
                         !(translation.Instructions ?? []).SequenceEqual(text.Instructions) ||
                         !(translation.Tips ?? []).SequenceEqual(text.Tips)) texts.Add($"{language}:different");
                else if (translation.Status != "published") texts.Add($"{language}:unpublished");
            }

            if (engine.Count + content.Count + texts.Count > 0) differences.Add(new(entry.Id, engine, content, texts));
        }

        var outside = rows.Where(r => !seedIds.Contains(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new ExerciseOutsideSeed(r.Id, r.Source, r.IsActive)).ToList();
        var invalid = rows.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => (Row: r, Validation: ExerciseMetadataValidator.Validate(Metadata(r))))
            .Where(x => !x.Validation.IsValid)
            .Select(x => new InvalidExercise(x.Row.Id, x.Row.Source, x.Row.IsActive, x.Validation.Errors)).ToList();

        return new ExerciseSeedReport(seed.Entries.Count, rows.Count, missing, differences, outside, invalid, Coverage(seed));
    }

    /// <summary>For every language of the app other than Spanish, how much of the seed it covers.</summary>
    public static IReadOnlyList<SeedLanguageCoverage> Coverage(ExerciseSeed seed) =>
        Enum.GetValues<AppLanguage>().Where(l => l != AppLanguage.Es).Select(l => l.Code()).Order(StringComparer.Ordinal)
            .Select(language =>
            {
                seed.Translations.TryGetValue(language, out var texts);
                var complete = seed.Entries.Count(e => texts is not null && texts.TryGetValue(e.Id, out var text) &&
                    ExerciseMetadataValidator.ValidateText(text).Count == 0);
                return new SeedLanguageCoverage(language, complete, seed.Entries.Count - complete);
            }).ToList();

    public static ExerciseMetadata Metadata(ExerciseCatalogItem item) => new(
        item.Id, item.NameEs, item.NameEn, item.DescriptionEs, item.InstructionsEs, item.TipsEs, item.ForceType, item.Mechanic,
        item.Difficulty, item.Equipment, item.BodyPart, item.Met, item.IsUnilateral, item.IsBodyweight, item.MovementPattern,
        item.PrimaryMuscles, item.SecondaryMuscles, item.MinimumExperience, item.SuitableGoals, item.RequiredEquipment,
        item.SelectionPriority);

    /// <summary>The texts the seed holds for one exercise: Spanish from the catalog file and the translation files.</summary>
    private static IEnumerable<(string Language, ExerciseText Text)> SeedTexts(ExerciseSeed seed, ExerciseSeedEntry entry)
    {
        yield return ("es", new ExerciseText(entry.NameEs, entry.DescriptionEs, entry.InstructionsEs, entry.TipsEs));
        foreach (var (language, texts) in seed.Translations)
            if (texts.TryGetValue(entry.Id, out var text)) yield return (language, text);
    }

    private static void Check(List<string> fields, string field, bool same)
    {
        if (!same) fields.Add(field);
    }

    private static bool SameSet(string[] expected, string[] actual) =>
        expected.Order(StringComparer.Ordinal).SequenceEqual(actual.Order(StringComparer.Ordinal));
}
