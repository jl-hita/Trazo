using System.Text.Json;

namespace Trazo.Application.Planning;

/// <summary>The stored form of the answers about each muscle: <c>[{"muscle":"Chest","recovery":"recovered","workload":"low"}]</c>.</summary>
public static class MuscleAnswers
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record Row(string? Muscle, string? Recovery, string? Workload);

    public static string? Serialize(IReadOnlyDictionary<string, MuscleFeedbackInput>? answers) =>
        answers is { Count: > 0 }
            ? JsonSerializer.Serialize(answers.Select(a => new Row(a.Key, a.Value.Recovery, a.Value.Workload)), Options)
            : null;

    /// <summary>The answers by muscle name; empty when there are none or the text cannot be read (never evidence for a change).</summary>
    public static IReadOnlyDictionary<string, MuscleFeedbackInput> Read(string? json)
    {
        var result = new Dictionary<string, MuscleFeedbackInput>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            foreach (var row in JsonSerializer.Deserialize<Row?[]>(json, Options) ?? [])
                if (row?.Muscle is { Length: > 0 } muscle) result.TryAdd(muscle, new MuscleFeedbackInput(row.Recovery, row.Workload));
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>The muscles the person said had not recovered when they came to train them.</summary>
    public static IReadOnlySet<string> NotRecovered(string? json) =>
        Read(json).Where(a => string.Equals(a.Value.Recovery, "notRecovered", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
}
