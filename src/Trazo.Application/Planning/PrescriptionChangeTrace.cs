using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trazo.Application.Planning;

/// <summary>What happened to one planned session in one operation. <see cref="Before"/> is null when the session was created, <see cref="After"/> when it was removed.</summary>
public sealed record SessionChange(
    string Operation, Guid SessionId, Guid MesocycleId, int WeekNumber, int SequenceIndex, string? Before, string? After);

/// <summary>
/// The structured record of the sessions a prescription change touched, stored in <c>PlanAdaptation.ChangesJson</c>. It carries the exact
/// prescription before and after (as the session stored it), so what changed can be reconstructed without the current plan or catalog.
/// </summary>
public static class PrescriptionChangeTrace
{
    /// <summary>Layout version of <c>PlanAdaptation.ChangesJson</c>.</summary>
    public const int SchemaVersion = 1;
    /// <summary>Layout version of <c>PlannedSession.OriginalPrescriptionJson</c>: the engine's <see cref="PrescriptionJson"/> layout, copied verbatim.</summary>
    public const int SnapshotSchemaVersion = 1;

    public const string Modified = "modified";
    public const string Added = "added";
    public const string Removed = "removed";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<SessionChange> changes) =>
        Build(changes).ToJsonString(Options);

    /// <summary>Adds sessions to a record already stored (the same operation reaching more sessions). The entries already there are kept as they are.</summary>
    public static string Append(string? existing, IEnumerable<SessionChange> changes)
    {
        var root = existing is null ? Build([]) : JsonNode.Parse(existing)!.AsObject();
        var sessions = root["sessions"] as JsonArray ?? [];
        foreach (var entry in Entries(changes)) sessions.Add(entry);
        root["sessions"] = sessions;
        return root.ToJsonString(Options);
    }

    /// <summary>The sessions recorded in a stored <c>ChangesJson</c>, in the order they were written.</summary>
    public static IReadOnlyList<SessionChange> Read(string json)
    {
        var result = new List<SessionChange>();
        if (JsonNode.Parse(json) is not JsonObject { } root || root["sessions"] is not JsonArray sessions) return result;
        foreach (var item in sessions.OfType<JsonObject>())
            result.Add(new SessionChange(
                (string)item["operation"]!, Guid.Parse((string)item["sessionId"]!), Guid.Parse((string)item["mesocycleId"]!),
                (int)item["weekNumber"]!, (int)item["sequenceIndex"]!, item["before"]?.ToJsonString(), item["after"]?.ToJsonString()));
        return result;
    }

    private static JsonObject Build(IEnumerable<SessionChange> changes)
    {
        var sessions = new JsonArray();
        foreach (var entry in Entries(changes)) sessions.Add(entry);
        return new JsonObject { ["schemaVersion"] = SchemaVersion, ["sessions"] = sessions };
    }

    private static IEnumerable<JsonNode> Entries(IEnumerable<SessionChange> changes) =>
        changes.Select(c => (JsonNode)new JsonObject
        {
            ["operation"] = c.Operation, ["sessionId"] = c.SessionId, ["mesocycleId"] = c.MesocycleId,
            ["weekNumber"] = c.WeekNumber, ["sequenceIndex"] = c.SequenceIndex,
            ["before"] = Embed(c.Before), ["after"] = Embed(c.After)
        });

    /// <summary>The prescription is embedded as JSON (not as an escaped string) so the record can be queried; unreadable text is kept as text.</summary>
    private static JsonNode? Embed(string? json)
    {
        if (json is null) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return JsonValue.Create(json); }
    }
}
