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
