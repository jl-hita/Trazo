using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// What the user fixed on one exercise of a session of the plan. An absent field was left to the engine. <see cref="Added"/>
/// says the user put the exercise there, so it is brought back when a later block chose another one.
/// </summary>
public sealed record ExercisePin(
    string ExerciseId, bool Added = false, int? Sets = null, int? MinRepetitions = null, int? MaxRepetitions = null,
    decimal? TargetRir = null, int? RestSeconds = null, TrainingTechniquePrescription? Technique = null, bool NoTechnique = false);

/// <summary>A superset the user made: its exercises, in order, and how it is alternated.</summary>
public sealed record SupersetPin(IReadOnlyList<string> Exercises, TrainingSupersetPrescription Rounds);

/// <summary>
/// The user's lasting decisions for one session of the week (identified by its title). <see cref="Order"/> and
/// <see cref="Supersets"/> are null while the user has not changed them.
/// </summary>
public sealed record SessionAdjustment(
    IReadOnlyList<string> Removed, IReadOnlyList<ExercisePin> Pins,
    IReadOnlyList<string>? Order = null, IReadOnlyList<SupersetPin>? Supersets = null)
{
    public static SessionAdjustment None { get; } = new([], []);
    public bool IsEmpty => Removed.Count == 0 && Pins.Count == 0 && Order is null && Supersets is null;
}

/// <summary>A prescription after the user's decisions, and the seconds it takes more (or less) than before.</summary>
public sealed record AdjustedPrescription(string Json, int ExtraSeconds);

/// <summary>
/// The layer of the user's decisions over a generated plan (docs/plan-customisation.md). Precedence: safety (limitations and
/// excluded exercises) &gt; the user &gt; automatic adaptation &gt; the engine's proposal. It edits the stored JSON, so what it does
/// not touch (target loads, marks of other operations) stays.
/// </summary>
public static class UserAdjustments
{
    /// <summary>The fields of an exercise the user fixed, written beside the engine's fields in the stored prescription.</summary>
    public const string FixedMark = "userFixed";

    private const string SetsField = "sets";
    private const string MinField = "minRepetitions";
    private const string MaxField = "maxRepetitions";
    private const string RirField = "targetRepetitionsInReserve";
    private const string RestField = "restSeconds";
    private const string TechniqueField = "technique";
    private const string GroupField = "supersetGroup";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(SessionAdjustment adjustment) => JsonSerializer.Serialize(adjustment, Options);

    public static SessionAdjustment Read(string json) =>
        JsonSerializer.Deserialize<SessionAdjustment>(json, Options) ?? SessionAdjustment.None;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What changed between the session as it is and the session the user wants, as decisions: the exercises taken out, the
    /// fields set to something else, and the order or the supersets when they differ.
    /// </summary>
    public static SessionAdjustment Between(string currentJson, ManualDay wanted)
    {
        var current = PrescriptionJson.Read(currentJson).OrderBy(e => e.Order).ToList();
        var removed = current.Select(e => e.ExerciseId).Where(id => !wanted.Exercises.Any(w => Same(w.ExerciseId, id))).ToList();
        var pins = new List<ExercisePin>();
        foreach (var exercise in wanted.Exercises)
        {
            var before = current.FirstOrDefault(e => Same(e.ExerciseId, exercise.ExerciseId));
            if (before is null)
            {
                pins.Add(new(exercise.ExerciseId, true, exercise.Sets, exercise.MinRepetitions, exercise.MaxRepetitions,
                    exercise.TargetRir, exercise.RestSeconds, exercise.Technique, exercise.Technique is null));
                continue;
            }
            // The reason the generator gave is not part of what the user chooses.
            var technique = before.Technique is { } had ? had with { Reason = null } : null;
            var changedTechnique = technique != (exercise.Technique is { } now ? now with { Reason = null } : null);
            var pin = new ExercisePin(before.ExerciseId, false,
                exercise.Sets != before.Sets ? exercise.Sets : null,
                exercise.MinRepetitions != before.MinRepetitions ? exercise.MinRepetitions : null,
                exercise.MaxRepetitions != before.MaxRepetitions ? exercise.MaxRepetitions : null,
                exercise.TargetRir is { } rir && rir != before.TargetRepetitionsInReserve ? rir : null,
                exercise.RestSeconds != before.RestSeconds ? exercise.RestSeconds : null,
                changedTechnique ? exercise.Technique : null,
                changedTechnique && exercise.Technique is null);
            if (pin != new ExercisePin(before.ExerciseId)) pins.Add(pin);
        }
        var kept = current.Select(e => e.ExerciseId).Where(id => wanted.Exercises.Any(w => Same(w.ExerciseId, id))).ToList();
        var keptWanted = wanted.Exercises.Select(e => e.ExerciseId).Where(id => kept.Any(k => Same(k, id))).ToList();
        var moved = !kept.SequenceEqual(keptWanted, StringComparer.OrdinalIgnoreCase) || pins.Any(p => p.Added);
        var groupsBefore = Describe(current.Select(e => (e.ExerciseId, PrescriptionJson.SupersetGroup(currentJson, e.ExerciseId))),
            PrescriptionJson.Supersets(currentJson));
        var groupsWanted = Describe(wanted.Exercises.Select(e => (e.ExerciseId, e.SupersetGroup)), wanted.Supersets ?? []);
        var supersets = SameSupersets(groupsBefore, groupsWanted) ? null : groupsWanted;
        return new(removed, pins, moved ? wanted.Exercises.Select(e => e.ExerciseId).ToList() : null, supersets);
    }

    private static List<SupersetPin> Describe(
        IEnumerable<(string Id, string? Group)> exercises, IReadOnlyList<TrainingSupersetPrescription> supersets) =>
        exercises.Where(e => e.Group is not null).GroupBy(e => e.Group!).Select(g =>
        {
            var ids = g.Select(e => e.Id).ToList();
            var rounds = supersets.FirstOrDefault(s => s.Group == g.Key) is { } found
                ? found with { Reason = null }
                : new TrainingSupersetPrescription(g.Key, 0, TechniqueRules.DefaultRestBetweenSeconds, TechniqueRules.DefaultRestAfterRoundSeconds);
            return new SupersetPin(ids, rounds);
        }).ToList();

    private static bool SameSupersets(IReadOnlyList<SupersetPin> a, IReadOnlyList<SupersetPin> b) =>
        a.Count == b.Count && a.All(x => b.Any(y => x.Rounds == y.Rounds && x.Exercises.SequenceEqual(y.Exercises, StringComparer.OrdinalIgnoreCase)));

    /// <summary>The decisions kept after a new edit: the newer one wins, and what the user put back is no longer removed.</summary>
    public static SessionAdjustment Merge(SessionAdjustment older, SessionAdjustment newer, IReadOnlyList<string> wantedIds)
    {
        bool Wanted(string id) => wantedIds.Any(w => Same(w, id));
        var removed = older.Removed.Concat(newer.Removed).Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !Wanted(id)).ToList();
        var pins = older.Pins.Where(p => !newer.Removed.Any(r => Same(r, p.ExerciseId))).ToList();
        foreach (var pin in newer.Pins)
        {
            var index = pins.FindIndex(p => Same(p.ExerciseId, pin.ExerciseId));
            if (index < 0)
            {
                pins.Add(pin);
                continue;
            }
            var was = pins[index];
            pins[index] = new(was.ExerciseId, was.Added || pin.Added, pin.Sets ?? was.Sets, pin.MinRepetitions ?? was.MinRepetitions,
                pin.MaxRepetitions ?? was.MaxRepetitions, pin.TargetRir ?? was.TargetRir, pin.RestSeconds ?? was.RestSeconds,
                pin.NoTechnique ? null : pin.Technique ?? was.Technique, pin.Technique is null && (pin.NoTechnique || was.NoTechnique));
        }
        var order = newer.Order ?? (older.Order is null ? null : wantedIds.ToList());
        return new(removed, pins, order, newer.Supersets ?? older.Supersets?
            .Where(s => !s.Exercises.Any(id => newer.Removed.Any(r => Same(r, id)))).ToList());
    }

    /// <summary>
    /// Applies the decisions to a session. <paramref name="lightBlock"/> (a deload or a preparation) keeps the choice and order
    /// of exercises but not the dose, techniques or supersets: those weeks are deliberately easier.
    /// </summary>
    public static AdjustedPrescription Apply(
        string json, SessionAdjustment adjustment, IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlySet<string> excluded, bool lightBlock = false)
    {
        if (adjustment.IsEmpty) return new(json, 0);
        var root = JsonNode.Parse(json)!.AsObject();
        var items = root["exercises"]!.AsArray().OfType<JsonObject>().Select(i => i.DeepClone().AsObject())
            .OrderBy(i => (int?)i["order"] ?? 0).ToList();
        var candidates = catalog.GroupBy(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var secondsBefore = Seconds(root, items);

        // 1. Bring back the exercises the user chose, in the place of the one that does their job.
        foreach (var pin in adjustment.Pins)
        {
            if (items.Any(i => Same(Id(i), pin.ExerciseId)) || excluded.Contains(pin.ExerciseId) ||
                !candidates.TryGetValue(pin.ExerciseId, out var candidate)) continue;
            bool DoesItsJob(JsonObject item) =>
                !adjustment.Pins.Any(p => Same(p.ExerciseId, Id(item))) &&
                candidates.TryGetValue(Id(item), out var other) && other.MovementPattern == candidate.MovementPattern &&
                other.PrimaryMuscleGroups.Intersect(candidate.PrimaryMuscleGroups).Any();
            var target = items.FirstOrDefault(i => adjustment.Removed.Any(r => Same(r, Id(i))) && DoesItsJob(i))
                         ?? items.FirstOrDefault(DoesItsJob);
            // A dose the user fixed on an exercise that the block dropped is not a reason to add one more exercise.
            if (target is null && !pin.Added) continue;
            var built = Build(candidate, pin, target ?? items.LastOrDefault());
            if (target is null) items.Add(built);
            else
            {
                if (target[GroupField] is { } group) built[GroupField] = group.DeepClone();
                items[items.IndexOf(target)] = built;
            }
        }

        // 2. Take out what the user removed, never leaving an empty session.
        var keep = items.Where(i => !adjustment.Removed.Any(r => Same(r, Id(i)))).ToList();
        if (keep.Count > 0) items = keep;

        // 3. The fields the user fixed.
        foreach (var item in items)
        {
            if (adjustment.Pins.FirstOrDefault(p => Same(p.ExerciseId, Id(item))) is not { } pin || lightBlock) continue;
            var marks = (item[FixedMark] as JsonArray)?.Select(m => (string)m!).ToHashSet() ?? [];
            void Fix(string field, JsonNode? value)
            {
                if (value is null) return;
                item[field] = value;
                marks.Add(field);
            }
            Fix(SetsField, pin.Sets is { } sets ? JsonValue.Create(sets) : null);
            Fix(MinField, pin.MinRepetitions is { } min ? JsonValue.Create(min) : null);
            Fix(MaxField, pin.MaxRepetitions is { } max ? JsonValue.Create(max) : null);
            Fix(RirField, pin.TargetRir is { } rir ? JsonValue.Create(rir) : null);
            Fix(RestField, pin.RestSeconds is { } rest ? JsonValue.Create(rest) : null);
            if ((int)item[MinField]! > (int)item[MaxField]!) item[MaxField] = (int)item[MinField]!;
            if (pin.NoTechnique)
            {
                item.Remove(TechniqueField);
                marks.Add(TechniqueField);
            }
            else if (pin.Technique is { } technique && candidates.TryGetValue(Id(item), out var candidate) &&
                     (candidate.AllowedTechniques?.Contains(technique.Type) ?? false))
                Fix(TechniqueField, JsonSerializer.SerializeToNode(technique, Options));
            if (marks.Count > 0) item[FixedMark] = new JsonArray(marks.Order(StringComparer.Ordinal).Select(m => (JsonNode?)JsonValue.Create(m)).ToArray());
        }

        // 4. The order.
        if (adjustment.Order is { } order)
        {
            var wanted = order.ToList();
            int Rank(JsonObject item) => wanted.FindIndex(id => Same(id, Id(item)));
            var ranked = items.Where(i => Rank(i) >= 0).OrderBy(Rank).ToList();
            var next = 0;
            items = items.Select(i => Rank(i) >= 0 ? ranked[next++] : i).ToList();
        }

        // 5. The supersets.
        var supersets = root["supersets"] is JsonArray stored
            ? stored.Deserialize<List<TrainingSupersetPrescription>>(Options) ?? []
            : [];
        if (adjustment.Supersets is { } mine && !lightBlock)
        {
            foreach (var item in items) item.Remove(GroupField);
            supersets = [];
            foreach (var superset in mine)
            {
                var members = superset.Exercises.Select(id => items.FirstOrDefault(i => Same(Id(i), id))).ToList();
                if (members.Count < 2 || members.Any(m => m is null)) continue;
                var rest = items.Where(i => !members.Skip(1).Contains(i)).ToList();
                rest.InsertRange(rest.IndexOf(members[0]!) + 1, members.Skip(1)!);
                items = rest;
                foreach (var member in members)
                {
                    member![GroupField] = superset.Rounds.Group;
                    member.Remove(TechniqueField);
                }
                if (superset.Rounds.Rounds > 0) supersets.Add(superset.Rounds);
            }
        }
        // A superset left with one exercise is no longer one.
        foreach (var group in items.Where(i => i[GroupField] is not null).GroupBy(i => (string)i[GroupField]!).ToList())
            if (group.Count() < 2)
            {
                foreach (var item in group) item.Remove(GroupField);
                supersets.RemoveAll(s => s.Group == group.Key);
            }
        supersets.RemoveAll(s => !items.Any(i => (string?)i[GroupField] == s.Group));

        for (var i = 0; i < items.Count; i++) items[i]["order"] = i + 1;
        root["exercises"] = new JsonArray(items.Cast<JsonNode?>().ToArray());
        root.Remove("supersets");
        if (supersets.Count > 0) root["supersets"] = JsonSerializer.SerializeToNode(supersets, Options);
        if (supersets.Count > 0 || items.Any(i => i[TechniqueField] is not null)) root["prescriptionVersion"] = PrescriptionJson.TechniqueVersion;
        else root.Remove("prescriptionVersion");
        return new(root.ToJsonString(Options), Seconds(root, items) - secondsBefore);
    }

    private static string Id(JsonObject item) => (string)item["exerciseId"]!;

    private static int Seconds(JsonObject root, IReadOnlyList<JsonObject> items) =>
        TechniqueRules.SessionSeconds(
            items.Select(i => ((string?)i[GroupField], (int)i[SetsField]!, (int)i[RestField]!,
                i[TechniqueField]?.Deserialize<TrainingTechniquePrescription>(Options))).ToList(),
            root["supersets"] is JsonArray array ? array.Deserialize<List<TrainingSupersetPrescription>>(Options) ?? [] : []);

    /// <summary>A new exercise of the session; what the user did not say comes from the one it stands in for.</summary>
    private static JsonObject Build(TrainingExerciseCandidate candidate, ExercisePin pin, JsonObject? like)
    {
        int Field(int? chosen, string name, int otherwise) => chosen ?? (like is null ? otherwise : (int?)like[name] ?? otherwise);
        var min = Field(pin.MinRepetitions, MinField, 8);
        var exercise = new TrainingExercisePrescription(
            candidate.ExerciseId, candidate.CatalogVersion, candidate.Name, (int?)like?["order"] ?? 1,
            Field(pin.Sets, SetsField, 3), min, Math.Max(min, Field(pin.MaxRepetitions, MaxField, 12)),
            pin.TargetRir ?? (like?[RirField] is { } rir ? (decimal?)rir : 2m), Field(pin.RestSeconds, RestField, 90),
            TrainingProgressionRule.DoubleProgression, "Elegido por el usuario.",
            candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups,
            EffectiveSecondaryMuscleGroups: candidate.EffectiveSecondaryMuscleGroups ?? []);
        return JsonSerializer.SerializeToNode(exercise, Options)!.AsObject();
    }

    /// <summary>
    /// What an automatic change leaves of the user's decisions: the fields they fixed go back to what they were, except on the
    /// exercises in <paramref name="unsafeIds"/> (pending pain), where safety comes first. <paramref name="kept"/> lists the
    /// exercises the change was not applied to as proposed.
    /// </summary>
    public static string KeepFixed(string before, string after, Func<string, bool> unsafeIds, out IReadOnlyList<string> kept)
    {
        var held = new List<string>();
        kept = held;
        if (!before.Contains(FixedMark, StringComparison.Ordinal)) return after;
        var old = JsonNode.Parse(before)!["exercises"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(Id, StringComparer.OrdinalIgnoreCase);
        var root = JsonNode.Parse(after)!.AsObject();
        foreach (var item in root["exercises"]!.AsArray().OfType<JsonObject>())
        {
            if (!old.TryGetValue(Id(item), out var was) || was[FixedMark] is not JsonArray marks || unsafeIds(Id(item))) continue;
            foreach (var field in marks.Select(m => (string)m!))
            {
                if (JsonNode.DeepEquals(item[field], was[field])) continue;
                if (was[field] is { } value) item[field] = value.DeepClone();
                else item.Remove(field);
                if (!held.Contains(Id(item))) held.Add(Id(item));
            }
        }
        return held.Count == 0 ? after : root.ToJsonString(Options);
    }
}
