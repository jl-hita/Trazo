using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Accounts;

/// <summary>
/// Erases the health data of one user when the consent is withdrawn: injury areas, pain and discomfort marks, illness as a reason and the
/// records derived from them. The training history itself (sessions, sets, loads) stays. Free-text notes are the user's own words and are
/// not rewritten; deleting the account removes them. See docs/account-deletion.md.
/// </summary>
internal static class HealthDataEraser
{
    private const string LimitationsKind = "limitations";
    /// <summary>Words the engine's stored explanations use when pain or an injury caused them.</summary>
    private static readonly string[] HealthWords = ["dolor", "molestia", "lesion", "lesión", "lesiona"];
    private static readonly string[] HealthSkipReasons = ["pain", "illness"];

    /// <summary>Changes the tracked rows; the caller saves them inside its transaction.</summary>
    public static async Task EraseAsync(TrazoDbContext db, Guid userId, CancellationToken ct)
    {
        var plans = await db.TrainingPlans.Where(p => p.UserId == userId).ToListAsync(ct);
        foreach (var plan in plans) plan.Limitations = [];
        var planIds = plans.Select(p => p.Id).ToList();

        // The trace of each generated block copies the profile, injury areas and pain-based decisions included.
        foreach (var block in await db.TrainingMesocycles.Where(m => planIds.Contains(m.PlanId) && m.GenerationSnapshotJson != null).ToListAsync(ct))
            block.GenerationSnapshotJson = Scrub(block.GenerationSnapshotJson);

        var logs = await db.WorkoutLogs.Include(l => l.Sets).Where(l => l.UserId == userId).ToListAsync(ct);
        foreach (var log in logs)
        {
            log.PainReported = false;
            if (log.NonCompletionReason == "Illness") log.NonCompletionReason = "Other";
            log.ExerciseFeedbackJson = Scrub(log.ExerciseFeedbackJson);
            foreach (var set in log.Sets)
            {
                set.PainReported = false;
                if (set.SkippedReason is { } reason && HealthSkipReasons.Contains(reason)) set.SkippedReason = "other";
            }
        }

        foreach (var adaptation in await db.PlanAdaptations.Where(a => planIds.Contains(a.PlanId)).ToListAsync(ct))
        {
            if (adaptation.Kind == LimitationsKind || MentionsHealth(adaptation.Explanation)) db.PlanAdaptations.Remove(adaptation);
            else adaptation.Explanation = adaptation.Explanation.Replace("(Illness)", "(Other)", StringComparison.Ordinal);
        }

        foreach (var observation in await db.MuscleWeekObservations.Where(o => o.UserId == userId).ToListAsync(ct))
        {
            observation.PainSessions = 0;
            observation.DiscomfortAnswers = 0;
            if (observation.Reason == "pain") observation.Reason = null;
        }

        foreach (var decision in await db.LongitudinalDecisions.Where(d => d.UserId == userId).ToListAsync(ct))
        {
            if (MentionsHealth(decision.Reason)) db.LongitudinalDecisions.Remove(decision);
            else decision.SignalsJson = Scrub(decision.SignalsJson)!;
        }
    }

    private static bool MentionsHealth(string text) => HealthWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Empties every property about pain, discomfort or injuries, at any depth. Numbers become 0, flags false and lists empty, so the
    /// document keeps the shape its readers expect; a discomfort answer is removed. Text that is not JSON is returned as it was.
    /// </summary>
    internal static string? Scrub(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        return Visit(root) ? root!.ToJsonString() : json;
    }

    private static bool Visit(JsonNode? node)
    {
        var changed = false;
        if (node is JsonArray array)
        {
            foreach (var item in array) changed |= Visit(item);
        }
        else if (node is JsonObject map)
        {
            foreach (var (name, value) in map.ToList())
            {
                if (name.Equals("discomfort", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= map.Remove(name);
                }
                else if (IsHealthName(name))
                {
                    JsonNode? empty = value switch
                    {
                        JsonArray => new JsonArray(),
                        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => JsonValue.Create(false),
                        JsonValue v when v.GetValueKind() is JsonValueKind.Number => JsonValue.Create(0),
                        _ => null
                    };
                    map[name] = empty;
                    changed = true;
                }
                else
                {
                    changed |= Visit(value);
                }
            }
        }
        return changed;
    }

    // "painSessionWindow" and the like are settings of the engine, the same for everybody.
    private static bool IsHealthName(string name) =>
        !name.EndsWith("Window", StringComparison.OrdinalIgnoreCase)
        && (name.Contains("pain", StringComparison.OrdinalIgnoreCase) || name.Contains("discomfort", StringComparison.OrdinalIgnoreCase)
            || name.Equals("limitations", StringComparison.OrdinalIgnoreCase));
}
