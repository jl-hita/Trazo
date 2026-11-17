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
}
