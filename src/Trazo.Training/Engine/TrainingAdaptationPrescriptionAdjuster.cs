using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trazo.Training;

/// <summary>
/// Applies an explicitly reviewed, exercise-ID-targeted volume reduction to future-session JSON.
/// It never changes completed history or infers targets from names or unrelated exercise metadata.
/// </summary>
public static class TrainingAdaptationPrescriptionAdjuster
{
    public static bool TryReduceSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription) =>
        TryReduceSets(json, targetExerciseIds, out updatedJson, out legacyPrescription, out _);

    public static bool TryReduceSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription,
        out IReadOnlySet<string> reducedExerciseIds)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        var reduced = new HashSet<string>(StringComparer.Ordinal);
        reducedExerciseIds = reduced;
        if (targetExerciseIds is null || targetExerciseIds.Count == 0)
            return false;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises)
                return false;

            var changed = false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise || exercise["sets"] is not JsonValue setsNode ||
                    !setsNode.TryGetValue<int>(out var sets) || sets <= 1)
                    continue;

                // The adapter proposes changes for stable exercise IDs only. Do not infer a
                // target from names or modify a different exercise with similar wording.
                var exerciseId = ReadString(exercise, "exerciseId", string.Empty);
                if (string.IsNullOrWhiteSpace(exerciseId) || !targetExerciseIds.Contains(exerciseId))
                    continue;

                // Remember the plan's original dose the first time it is reduced, so volume can later be
                // restored (one set at a time) when the fatigue signal disappears.
                exercise["adaptationOriginalSets"] ??= sets;
                exercise["sets"] = sets - 1;
                reduced.Add(exerciseId);
                changed = true;
            }

            if (!changed)
                return false;

            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            if (string.IsNullOrWhiteSpace(legacyPrescription))
                return false;

            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Gives back one set to exercises whose volume was previously reduced by an approved adaptation
    /// (marker <c>adaptationOriginalSets</c>), never above the original dose. The marker is removed
    /// once the original dose is reached.
    /// </summary>
    public static bool TryRestoreSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription,
        out IReadOnlySet<string> restoredExerciseIds)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        var restored = new HashSet<string>(StringComparer.Ordinal);
        restoredExerciseIds = restored;
        if (targetExerciseIds is null || targetExerciseIds.Count == 0) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise ||
                    exercise["sets"] is not JsonValue setsNode || !setsNode.TryGetValue<int>(out var sets) ||
                    exercise["adaptationOriginalSets"] is not JsonValue originalNode ||
                    !originalNode.TryGetValue<int>(out var original) || sets >= original)
                    continue;
                var exerciseId = ReadString(exercise, "exerciseId", string.Empty);
                if (string.IsNullOrWhiteSpace(exerciseId) || !targetExerciseIds.Contains(exerciseId)) continue;
                exercise["sets"] = sets + 1;
                if (sets + 1 >= original) exercise.Remove("adaptationOriginalSets");
                restored.Add(exerciseId);
            }
            if (restored.Count == 0) return false;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Lowers session intensity by asking for one more repetition in reserve (maximum 5) in every
    /// exercise that has a RIR target. Used when low recovery repeats but no single exercise can be
    /// blamed, so volume is left untouched. Internal product rule; the professional approves it.
    /// </summary>
    public static bool TryRaiseReserve(string json, out string updatedJson, out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            var changed = false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise ||
                    exercise["targetRepetitionsInReserve"] is not JsonValue rirNode ||
                    !rirNode.TryGetValue<decimal>(out var rir) || rir >= 5m)
                    continue;
                exercise["targetRepetitionsInReserve"] = Math.Min(5m, rir + 1m);
                changed = true;
            }
            if (!changed) return false;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Replaces one prescribed exercise by another in ONE session's JSON (patient exercise swap).
    /// Sets, repetition range, RIR, rest and progression rule are kept. Load and warm-up guidance
    /// belong to the old exercise, so they are removed; the origin is kept in
    /// <c>swappedFromExerciseId</c> so the professional can see and revert the change.
    /// Fails (returns false) if the exercise is missing, the replacement is already in the session
    /// or the JSON is not a valid structured prescription.
    /// </summary>
    public static bool TrySwapExercise(
        string json,
        string currentExerciseId,
        TrainingExerciseCandidate replacement,
        out string updatedJson,
        out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            var items = exercises.OfType<JsonObject>().ToArray();
            if (items.Any(item => string.Equals(ReadString(item, "exerciseId", string.Empty), replacement.ExerciseId, StringComparison.OrdinalIgnoreCase)))
                return false;
            var target = items.FirstOrDefault(item =>
                string.Equals(ReadString(item, "exerciseId", string.Empty), currentExerciseId, StringComparison.OrdinalIgnoreCase));
            if (target is null) return false;

            var originalName = ReadString(target, "exerciseNameSnapshot", "Ejercicio");
            target["swappedFromExerciseId"] ??= ReadString(target, "exerciseId", string.Empty);
            target["exerciseId"] = replacement.ExerciseId;
            target["catalogVersion"] = replacement.CatalogVersion;
            target["exerciseNameSnapshot"] = replacement.Name;
            target["primaryMuscleGroups"] = new JsonArray(replacement.PrimaryMuscleGroups.Select(m => (JsonNode?)JsonValue.Create(m.ToString())).ToArray());
            target["secondaryMuscleGroups"] = new JsonArray(replacement.SecondaryMuscleGroups.Select(m => (JsonNode?)JsonValue.Create(m.ToString())).ToArray());
            foreach (var stale in new[] { "targetLoadKg", "loadBasis", "warmupSets", "warmupNote", "adaptationOriginalSets" })
                target.Remove(stale);
            var rationale = $"Intercambio solicitado por el paciente: {originalName} → {replacement.Name} (mismo patrón o mismos músculos principales).";
            target["selectionRationale"] = rationale.Length <= 500 ? rationale : rationale[..500];

            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Turns one session into a conservative deload (same rule as the explicit deload of the generator):
    /// about half the sets (rounded up, minimum 1), +2 RIR (maximum 5) and no load progression.
    /// Marked with <c>adaptationDeload</c> so it is never applied twice. Used by the autonomous
    /// reactive deload; the user is told in the session instructions.
    /// </summary>
    public static bool TryApplyDeload(string json, out string updatedJson, out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            if (root["adaptationDeload"] is JsonValue marker && marker.TryGetValue<bool>(out var already) && already) return false;
            var changed = false;
            foreach (var item in exercises.OfType<JsonObject>())
            {
                if (item["sets"] is not JsonValue setsNode || !setsNode.TryGetValue<int>(out var sets)) continue;
                item["adaptationOriginalSets"] ??= sets;
                item["sets"] = Math.Max(1, (int)Math.Ceiling(sets * 0.5m));
                if (item["targetRepetitionsInReserve"] is JsonValue rirNode && rirNode.TryGetValue<decimal>(out var rir))
                    item["targetRepetitionsInReserve"] = Math.Min(5m, rir + 2m);
                item["progressionRule"] = (int)TrainingProgressionRule.None;
                changed = true;
            }
            if (!changed) return false;
            root["adaptationDeload"] = true;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>Adds or removes sets of one exercise (bounds 1..5). A reduction remembers the original dose.</summary>
    public static bool TryAdjustSets(string json, string exerciseId, int delta, out string updatedJson, out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            var target = exercises.OfType<JsonObject>().FirstOrDefault(item =>
                string.Equals(ReadString(item, "exerciseId", string.Empty), exerciseId, StringComparison.OrdinalIgnoreCase));
            if (target is null || target["sets"] is not JsonValue setsNode || !setsNode.TryGetValue<int>(out var sets)) return false;
            var next = sets + delta;
            if (next < 1 || next > 5) return false;
            if (delta < 0) target["adaptationOriginalSets"] ??= sets;
            target["sets"] = next;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>Writes a working load (and why) into one exercise of a future session.</summary>
    public static bool TrySetLoad(
        string json, string exerciseId, decimal loadKg, string basis, out string updatedJson, out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        if (loadKg <= 0 || loadKg > 1000) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            var target = exercises.OfType<JsonObject>().FirstOrDefault(item =>
                string.Equals(ReadString(item, "exerciseId", string.Empty), exerciseId, StringComparison.OrdinalIgnoreCase));
            if (target is null) return false;
            if (target["targetLoadKg"] is JsonValue current && current.TryGetValue<decimal>(out var existing) && existing == loadKg)
                return false;
            target["targetLoadKg"] = loadKg;
            target["loadBasis"] = basis.Length <= 300 ? basis : basis[..300];
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    private static string FormatExercise(JsonObject exercise)
    {
        var order = ReadInt(exercise, "order", 0);
        var name = ReadString(exercise, "exerciseNameSnapshot", "Ejercicio");
        var sets = ReadInt(exercise, "sets", 1);
        var min = ReadInt(exercise, "minRepetitions", 1);
        var max = ReadInt(exercise, "maxRepetitions", min);
        var rir = exercise["targetRepetitionsInReserve"] is JsonValue rirNode && rirNode.TryGetValue<decimal>(out var rirValue)
            ? rirValue.ToString() : "—";
        var rest = ReadInt(exercise, "restSeconds", 0);
        var progression = ReadProgression(exercise["progressionRule"]);
        var load = exercise["targetLoadKg"] is JsonValue loadNode && loadNode.TryGetValue<decimal>(out var loadKg) ? $" carga {loadKg} kg;" : string.Empty;
        var warmup = exercise["warmupSets"] is JsonValue warmNode && warmNode.TryGetValue<int>(out var warmSets) ? $" aproximación {warmSets} serie(s) muy fácil(es);" : string.Empty;
        var id = ReadString(exercise, "exerciseId", string.Empty);
        var version = ReadString(exercise, "catalogVersion", string.Empty);
        return $"{order}. {name} — {sets} × {min}-{max} reps; RIR {rir}; descanso {rest}s;{load}{warmup} progresión {progression}. [ID: {id}; catálogo: {version}]";
    }

    private static int ReadInt(JsonObject node, string key, int fallback) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
    private static string ReadString(JsonObject node, string key, string fallback) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : fallback;
    private static string ReadProgression(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var numeric) &&
            Enum.IsDefined(typeof(TrainingProgressionRule), numeric))
            return ((TrainingProgressionRule)numeric).ToString();
        return value?.ToString() ?? "—";
    }
}
