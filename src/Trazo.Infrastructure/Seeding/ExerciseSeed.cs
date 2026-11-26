using System.Text.Json;
using Trazo.Application.Catalog;
using Trazo.Domain.Training;

namespace Trazo.Infrastructure.Seeding;

/// <summary>One exercise of <c>Seeds/exercise-catalog.json</c>.</summary>
public sealed class ExerciseSeedEntry
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
    public string[]? EffectiveSecondaryMuscles { get; init; }
    public string[]? Emphasis { get; init; }
    public string[]? LimitationAreas { get; init; }
    public bool MetadataReviewed { get; init; }

    /// <summary>The entry as the catalog stores it, ready to be validated.</summary>
    public ExerciseMetadata Metadata() => new(
        Id, NameEs, NameEn, DescriptionEs, InstructionsEs, TipsEs, ForceType, Mechanic, Difficulty, Equipment, BodyPart, Met,
        IsUnilateral, IsBodyweight, MovementPattern, PrimaryMuscles, SecondaryMuscles, MinimumExperience, SuitableGoals,
        RequiredEquipment, SelectionPriority, EffectiveSecondaryMuscles, Emphasis, LimitationAreas, MetadataReviewed);
}

/// <summary>The versioned exercise library shipped with the application: the exercises and their translations.</summary>
public sealed record ExerciseSeed(
    IReadOnlyList<ExerciseSeedEntry> Entries,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ExerciseText>> Translations)
{
    private static readonly string[] Sources = ["repdb-free", "trazo-curated"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "Seeds");

    public static async Task<ExerciseSeed> LoadAsync(string folder, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(folder, "exercise-catalog.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The complete exercise catalog seed was not published with the API.", path);

        List<ExerciseSeedEntry> entries;
        await using (var stream = File.OpenRead(path))
            entries = await JsonSerializer.DeserializeAsync<List<ExerciseSeedEntry>>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Exercise catalog seed is empty or invalid.");

        // One file per language (translations/{code}.json), keyed by exercise id. Spanish lives in the catalog file itself.
        var translations = new SortedDictionary<string, IReadOnlyDictionary<string, ExerciseText>>(StringComparer.Ordinal);
        var texts = Path.Combine(folder, "translations");
        if (Directory.Exists(texts))
            foreach (var file in Directory.GetFiles(texts, "*.json").Order(StringComparer.Ordinal))
            {
                var language = Path.GetFileNameWithoutExtension(file);
                await using var stream = File.OpenRead(file);
                translations[language] = await JsonSerializer.DeserializeAsync<Dictionary<string, ExerciseText>>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException($"Exercise translations '{language}' are empty or invalid.");
            }

        return new ExerciseSeed(entries, translations);
    }

    /// <summary>The ids of the exercises the seed holds, without loading their translations.</summary>
    public static IReadOnlySet<string> LoadIds(string folder)
    {
        using var stream = File.OpenRead(Path.Combine(folder, "exercise-catalog.json"));
        var entries = JsonSerializer.Deserialize<List<ExerciseSeedEntry>>(stream, JsonOptions)
            ?? throw new InvalidDataException("Exercise catalog seed is empty or invalid.");
        return entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Everything wrong with the exercises of the seed, one line per exercise; empty when it can be applied.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Entries.Count == 0) problems.Add("The seed has no exercises.");
        problems.AddRange(Entries.GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: duplicated id."));
        foreach (var entry in Entries)
        {
            if (!Sources.Contains(entry.Source, StringComparer.Ordinal)) problems.Add($"{entry.Id}: unknown source '{entry.Source}'.");
            if (entry.Version < 1) problems.Add($"{entry.Id}: the version must be at least 1.");
            if (entry.EffectiveSecondaryMuscles is null || entry.Emphasis is null || entry.LimitationAreas is null)
                problems.Add($"{entry.Id}: the seed states effectiveSecondaryMuscles, emphasis and limitationAreas, even when empty.");
            var validation = ExerciseMetadataValidator.Validate(entry.Metadata());
            if (!validation.IsValid) problems.Add($"{entry.Id}: {validation.Describe()}");
        }
        return problems;
    }
}
