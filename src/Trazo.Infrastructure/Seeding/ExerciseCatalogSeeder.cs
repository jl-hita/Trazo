using System.Text.Json;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class SeedEntry
    {
        public string Id { get; init; } = "";
        public int Version { get; init; } = 1;
        public string Source { get; init; } = "";
        public string NameEs { get; init; } = "";
        public string NameEn { get; init; } = "";
        public string DescriptionEs { get; init; } = "";
        public string[] InstructionsEs { get; init; } = [];
        public string[] TipsEs { get; init; } = [];
        public string ForceType { get; init; } = "";
        public string Mechanic { get; init; } = "";
        public string Difficulty { get; init; } = "";
        public string? Equipment { get; init; }
        public string BodyPart { get; init; } = "";
        public double Met { get; init; }
        public bool IsUnilateral { get; init; }
        public bool IsBodyweight { get; init; }
        public string MovementPattern { get; init; } = "";
        public string[] PrimaryMuscles { get; init; } = [];
        public string[] SecondaryMuscles { get; init; } = [];
        public string MinimumExperience { get; init; } = "";
        public string[] SuitableGoals { get; init; } = [];
        public string[] RequiredEquipment { get; init; } = [];
        public int SelectionPriority { get; init; } = 50;
        public bool IsActive { get; init; } = true;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seeds", "exercise-catalog.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The complete exercise catalog seed was not published with the API.", path);

        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<SeedEntry>>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Exercise catalog seed is empty or invalid.");

        if (entries.Count == 0 || entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Exercise catalog seed is empty or contains duplicate exercise IDs.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731904221)", cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var updated = 0;
        var preserved = 0;
        var translationsAdded = 0;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) ||
                entry.Source is not ("repdb-free" or "trazo-curated") ||
                string.IsNullOrWhiteSpace(entry.NameEn) ||
                string.IsNullOrWhiteSpace(entry.NameEs) ||
                entry.Version < 1 ||
                entry.InstructionsEs.Length < 3 ||
                entry.InstructionsEs.Any(string.IsNullOrWhiteSpace) ||
                entry.PrimaryMuscles.Length == 0 ||
                entry.SuitableGoals.Length == 0 ||
                entry.SelectionPriority is < 0 or > 100 ||
                entry.RequiredEquipment.Any(value => string.Equals(value, "machine", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Exercise seed entry '{entry.Id}' failed validation.");
            }

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
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Complete exercise catalog seed processed: {SeedEntries} entries, {Inserted} inserted, {Updated} untouched rows refreshed, {Preserved} existing rows preserved, {TranslationsAdded} missing translations added.",
            entries.Count, inserted, updated, preserved, translationsAdded);
    }
}
