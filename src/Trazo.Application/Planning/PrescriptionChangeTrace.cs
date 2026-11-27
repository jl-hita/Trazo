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
    /// <summary>
    /// Layout version of <c>PlanAdaptation.ChangesJson</c>. 1: <c>sessions</c>. 2: adds the optional root <c>scope</c>, written when the user
    /// chose how far a change reaches (an exercise swap). A record without it has an unknown scope: it is never guessed from its sessions.
    /// </summary>
    public const int SchemaVersion = 2;
    /// <summary>Layout version of <c>PlannedSession.OriginalPrescriptionJson</c>: the engine's <see cref="PrescriptionJson"/> layout, copied verbatim.</summary>
    public const int SnapshotSchemaVersion = 1;

    public const string Modified = "modified";
    public const string Added = "added";
    public const string Removed = "removed";

    /// <summary>The user chose to change only the session they were looking at.</summary>
    public const string SessionScope = "session";
    /// <summary>The user chose that session and the pending ones after it in its block, however many that turned out to be.</summary>
    public const string BlockScope = "block";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<SessionChange> changes, string? scope = null)
    {
        var root = Build(changes);
        if (scope is not null) root["scope"] = scope;
        return root.ToJsonString(Options);
    }

    /// <summary>Adds sessions to a record already stored (the same operation reaching more sessions). The entries already there are kept as they are.</summary>
    public static string Append(string? existing, IEnumerable<SessionChange> changes)
    {
        var root = existing is null ? Build([]) : JsonNode.Parse(existing)!.AsObject();
        var sessions = root["sessions"] as JsonArray ?? [];
        foreach (var entry in Entries(changes)) sessions.Add(entry);
        root["sessions"] = sessions;
        root["schemaVersion"] = SchemaVersion; // the record is rewritten in the current layout, as its row says
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

    /// <summary>The scope recorded in a stored <c>ChangesJson</c>; null when there is none (older records, other operations) or it is not one of the known values.</summary>
    public static string? ReadScope(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) is JsonObject root && root["scope"] is JsonValue value && value.TryGetValue<string>(out var scope)
                && scope is SessionScope or BlockScope ? scope : null;
        }
        catch (JsonException) { return null; }
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
