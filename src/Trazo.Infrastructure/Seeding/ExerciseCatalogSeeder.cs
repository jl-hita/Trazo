using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Imports first-party curated exercise additions from the versioned JSON seed.
/// Existing rows are deliberately not overwritten: admin curation always wins.
/// Safe to execute on every startup and serialized across concurrent API instances.
/// </summary>
public sealed class ExerciseCatalogSeeder(
    TrazoDbContext db,
    ILogger<ExerciseCatalogSeeder> logger)
{
    private const string Source = "trazo-curated";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class SeedEntry
    {
        public string Id { get; init; } = "";
        public int Version { get; init; } = 1;
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
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seeds", "exercise-catalog-additions.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The required exercise catalog seed was not published with the API.", path);

        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<SeedEntry>>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Exercise catalog seed is empty or invalid.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Prevent two API instances from racing through the existence check during first deployment.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(731904221)", cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.NameEn) ||
                string.IsNullOrWhiteSpace(entry.NameEs) ||
                entry.Version < 1 ||
                entry.InstructionsEs.Length < 3 ||
                entry.InstructionsEs.Any(string.IsNullOrWhiteSpace) ||
                entry.PrimaryMuscles.Length == 0 ||
                entry.SuitableGoals.Length == 0 ||
                entry.SelectionPriority is < 0 or > 100)
            {
                throw new InvalidDataException($"Exercise seed entry '{entry.Id}' failed validation.");
            }

            var exists = await db.ExerciseCatalog.AnyAsync(x => x.Id == entry.Id, cancellationToken);
            if (exists)
                continue;

            db.ExerciseCatalog.Add(new ExerciseCatalogItem
            {
                Id = entry.Id,
                Source = Source,
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
                RequiredEquipment = entry.RequiredEquipment,
                SelectionPriority = entry.SelectionPriority,
                SeedVersion = entry.Version,
                CreatedAt = now,
                UpdatedAt = now
            });
            inserted++;
        }

        if (inserted > 0)
            await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Exercise catalog seed processed: {SeedEntries} entries, {Inserted} inserted; existing entries preserved.",
            entries.Count, inserted);
    }
}
