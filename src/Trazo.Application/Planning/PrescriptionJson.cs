using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// The stored form of a session prescription: <c>{"exercises":[...]}</c> in camelCase with enum names. It is the shape
/// <see cref="TrainingAdaptationPrescriptionAdjuster"/> edits (it also tolerates numeric enums, which the adjuster writes
/// for the progression rule).
/// </summary>
public static class PrescriptionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IEnumerable<TrainingExercisePrescription> exercises) =>
        JsonSerializer.Serialize(new { exercises }, Options);

    /// <summary>The layout version written when a prescription carries techniques or superset rounds.</summary>
    public const int TechniqueVersion = 2;

    /// <summary>Superset groups plus how each is alternated, and the version that says the session carries techniques.</summary>
    public static string Serialize(
        IEnumerable<TrainingExercisePrescription> exercises, IReadOnlyDictionary<string, string> supersetGroups,
        IReadOnlyCollection<TrainingSupersetPrescription> supersets)
    {
        var list = exercises.ToList();
        var json = Serialize(list, supersetGroups);
        if (supersets.Count == 0 && list.All(e => e.Technique is null)) return json;
        var root = JsonNode.Parse(json)!.AsObject();
        root["prescriptionVersion"] = TechniqueVersion;
        if (supersets.Count > 0) root["supersets"] = JsonSerializer.SerializeToNode(supersets, Options);
        return root.ToJsonString(Options);
    }

    /// <summary>True when the session carries a technique or superset rounds.</summary>
    public static bool HasTechniques(string json) => Supersets(json).Count > 0 || Read(json).Any(e => e.Technique is not null);

    /// <summary>How the supersets of a session are alternated; empty for sessions stored without it.</summary>
    public static IReadOnlyList<TrainingSupersetPrescription> Supersets(string json) =>
        JsonNode.Parse(json) is JsonObject root && root["supersets"] is JsonArray array
            ? array.Deserialize<List<TrainingSupersetPrescription>>(Options) ?? []
            : [];

    /// <summary>
    /// Serializes with superset groups (exercise id to group letter). The group is stored beside the engine's fields, like
    /// <c>swappedFromExerciseId</c>, so the engine's adjuster keeps it untouched when it edits the prescription.
    /// </summary>
    public static string Serialize(IEnumerable<TrainingExercisePrescription> exercises, IReadOnlyDictionary<string, string> supersetGroups)
    {
        var root = JsonNode.Parse(Serialize(exercises))!.AsObject();
        foreach (var item in root["exercises"]!.AsArray().OfType<JsonObject>())
            if (supersetGroups.TryGetValue((string)item["exerciseId"]!, out var group)) item["supersetGroup"] = group;
        return root.ToJsonString(Options);
    }

    /// <summary>
    /// Appends one exercise the user added to this single session. It is marked <c>extra</c>: it is not part of the program,
    /// so nothing copies it to other sessions. Everything else in the JSON (superset groups, swaps, adaptation marks) is kept.
    /// </summary>
    public static string AppendExtra(string json, TrainingExercisePrescription exercise)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var array = root["exercises"] as JsonArray ?? [];
        var item = JsonNode.Parse(JsonSerializer.Serialize(exercise, Options))!.AsObject();
        item["extra"] = true;
        array.Add(item);
        root["exercises"] = array;
        return root.ToJsonString(Options);
    }

    /// <summary>True when the user added the exercise to this session on top of the program.</summary>
    public static bool IsExtra(string json, string exerciseId)
    {
        if (JsonNode.Parse(json) is not JsonObject { } root || root["exercises"] is not JsonArray array) return false;
        return array.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals((string?)item["exerciseId"], exerciseId, StringComparison.OrdinalIgnoreCase))
            ?["extra"]?.GetValue<bool>() ?? false;
    }

    /// <summary>The superset group letter of an exercise, if it belongs to one.</summary>
    public static string? SupersetGroup(string json, string exerciseId)
    {
        if (JsonNode.Parse(json) is not JsonObject { } root || root["exercises"] is not JsonArray array) return null;
        return array.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals((string?)item["exerciseId"], exerciseId, StringComparison.OrdinalIgnoreCase))
            ?["supersetGroup"]?.GetValue<string>();
    }

    /// <summary>
    /// A prescription stored before it carried the secondary muscles that count, completed from today's catalog: those of the
    /// exercise's secondary muscles that the catalog marks as effective. The stored text is not changed, and a prescription that
    /// already says which count is returned as it is.
    /// </summary>
    public static string CompleteStimulus(string json, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        const string field = "effectiveSecondaryMuscleGroups";
        if (catalog is null || JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray array) return json;
        var changed = false;
        foreach (var item in array.OfType<JsonObject>())
        {
            if (item[field] is JsonArray || (string?)item["exerciseId"] is not { } id) continue;
            var effective = catalog.TryGetValue(id, out var found) ? found : [];
            var secondary = item["secondaryMuscleGroups"] as JsonArray ?? [];
            item[field] = new JsonArray(secondary.Select(m => m?.GetValueKind() == JsonValueKind.String ? (string?)m : null)
                .Where(m => m is not null && effective.Contains(m)).Select(m => (JsonNode?)JsonValue.Create(m)).ToArray());
            changed = true;
        }
        return changed ? root.ToJsonString(Options) : json;
    }

    public static IReadOnlyList<TrainingExercisePrescription> Read(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root?["exercises"] is not JsonArray array) return [];
        return array.Deserialize<List<TrainingExercisePrescription>>(Options) ?? [];
    }

    /// <summary>The exercise this one replaced in a swap, if any.</summary>
    public static string? SwappedFrom(string json, string exerciseId)
    {
        if (JsonNode.Parse(json) is not JsonObject { } root || root["exercises"] is not JsonArray array) return null;
        return array.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals((string?)item["exerciseId"], exerciseId, StringComparison.OrdinalIgnoreCase))
            ?["swappedFromExerciseId"]?.GetValue<string>();
    }

    /// <summary>The dose before an automatic reduction, per exercise (only exercises that were reduced).</summary>
    public static IReadOnlyDictionary<string, int> OriginalSets(string json)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray array) return result;
        foreach (var item in array.OfType<JsonObject>())
            if (item["exerciseId"] is JsonValue id && id.TryGetValue<string>(out var exerciseId)
                && item["adaptationOriginalSets"] is JsonValue original && original.TryGetValue<int>(out var sets))
                result[exerciseId] = sets;
        return result;
    }

    /// <summary>True when the session was turned into a deload by the automatic adaptation.</summary>
    public static bool IsAdaptationDeload(string json) =>
        JsonNode.Parse(json) is JsonObject root && root["adaptationDeload"] is JsonValue marker
        && marker.TryGetValue<bool>(out var value) && value;

    /// <summary>The absence (instant of the last workout, round-trip format) this session was already made lighter for, or null.</summary>
    public static string? EasedBackFor(string json) =>
        JsonNode.Parse(json) is JsonObject root && root["easedBackFor"] is JsonValue marker && marker.TryGetValue<string>(out var value) ? value : null;

    /// <summary>Marks the session as already lightened for the absence that started at <paramref name="absence"/>, so it is never done twice.</summary>
    public static string MarkEasedBack(string json, string absence)
    {
        var root = (JsonObject)JsonNode.Parse(json)!;
        root["easedBackFor"] = absence;
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
