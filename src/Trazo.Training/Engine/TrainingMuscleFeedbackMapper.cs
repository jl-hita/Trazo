using System.Text.Json;

namespace Trazo.Training;

/// <summary>
/// Maps the newest-first session history into chronological per-exercise muscle feedback.
/// Every prescribed exercise receives an exposure; missing answers are neutral, and an unusable
/// newest prescription blocks older history from being mistaken for recent evidence.
/// </summary>
public static class TrainingMuscleFeedbackMapper
{
    public static IReadOnlyList<TrainingMuscleFeedbackExposure> Map(
        IEnumerable<TrainingMuscleFeedbackRecord> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var records = newestFirst.ToArray();
        if (records.Length == 0)
            return Array.Empty<TrainingMuscleFeedbackExposure>();

        var parsed = new List<(TrainingMuscleFeedbackRecord Record, IReadOnlyList<PrescribedExercise> Exercises,
            IReadOnlyDictionary<string, JsonElement> Feedback)>();
        foreach (var record in records)
        {
            var exercises = ReadPrescription(record.PrescriptionJson);
            if (exercises is null || exercises.Count == 0)
            {
                // If the newest record is unusable, no older feedback can safely replace it.
                // If a later/older record is unusable, keep only the newer valid window.
                if (parsed.Count == 0)
                    return Array.Empty<TrainingMuscleFeedbackExposure>();
                break;
            }

            parsed.Add((record, exercises, ReadFeedback(record.ExerciseFeedbackJson)));
        }

        var exposures = new List<TrainingMuscleFeedbackExposure>();
        foreach (var item in parsed.AsEnumerable().Reverse())
        {
            var completed = string.Equals(item.Record.Outcome, "completed", StringComparison.OrdinalIgnoreCase);
            foreach (var exercise in item.Exercises)
            {
                if (exercise.EffectiveMuscles.Count == 0)
                    continue;

                item.Feedback.TryGetValue(exercise.ExerciseId, out var feedback);
                exposures.Add(new TrainingMuscleFeedbackExposure(
                    item.Record.Date,
                    exercise.ExerciseId,
                    exercise.EffectiveMuscles,
                    exercise.Sets,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "stimulus") : null,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "discomfort") : null,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "preFatigue") : null,
                    completed));
            }
        }

        return exposures;
    }

    /// <summary>
    /// Counts planned weekly sets using the same reviewed primary/secondary stimulus audit
    /// as the generator, so feedback-driven increases respect the effective-volume ceiling.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CountPlannedEffectiveSets(IEnumerable<string> prescriptionJsons)
    {
        ArgumentNullException.ThrowIfNull(prescriptionJsons);
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var json in prescriptionJsons)
        {
            var exercises = ReadPrescription(json);
            if (exercises is null) continue;
            foreach (var exercise in exercises)
            foreach (var muscle in exercise.EffectiveMuscles)
                totals[muscle] = totals.GetValueOrDefault(muscle) + exercise.Sets;
        }
        return totals;
    }

    private static IReadOnlyList<PrescribedExercise>? ReadPrescription(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("exercises", out var exercises) ||
                exercises.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<PrescribedExercise>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exercise in exercises.EnumerateArray())
            {
                if (exercise.ValueKind != JsonValueKind.Object)
                    return null;
                var id = ReadString(exercise, "exerciseId");
                if (string.IsNullOrWhiteSpace(id) ||
                    !exercise.TryGetProperty("sets", out var setsElement) ||
                    setsElement.ValueKind != JsonValueKind.Number ||
                    !setsElement.TryGetInt32(out var sets) || sets is < 1 or > 100 ||
                    !exercise.TryGetProperty("primaryMuscleGroups", out var primaryMusclesElement) ||
                    primaryMusclesElement.ValueKind != JsonValueKind.Array ||
                    !ids.Add(id))
                    return null;

                var primary = ReadMuscleGroups(primaryMusclesElement);
                var secondary = exercise.TryGetProperty("secondaryMuscleGroups", out var secondaryElement) &&
                                secondaryElement.ValueKind == JsonValueKind.Array
                    ? ReadMuscleGroups(secondaryElement)
                    : Array.Empty<TrainingMuscleGroup>();
                var effective = TrainingExerciseStimulus.GetEffectiveMuscles(id, primary, secondary)
                    .Select(muscle => muscle.ToString())
                    .OrderBy(muscle => muscle, StringComparer.Ordinal)
                    .ToArray();
                result.Add(new PrescribedExercise(id, sets, effective));
            }

            return result.Count == 0 ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TrainingMuscleGroup> ReadMuscleGroups(JsonElement values)
    {
        var muscles = new HashSet<TrainingMuscleGroup>();
        foreach (var value in values.EnumerateArray())
        {
            TrainingMuscleGroup? parsed = null;
            if (value.ValueKind == JsonValueKind.String &&
                Enum.TryParse<TrainingMuscleGroup>(value.GetString(), true, out var named) &&
                Enum.IsDefined(named))
                parsed = named;
            else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
            {
                var numbered = (TrainingMuscleGroup)numeric;
                if (Enum.IsDefined(numbered)) parsed = numbered;
            }

            if (parsed.HasValue) muscles.Add(parsed.Value);
        }
        return muscles.OrderBy(muscle => muscle).ToArray();
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadFeedback(string? json)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var exercise in document.RootElement.EnumerateArray())
            {
                if (exercise.ValueKind != JsonValueKind.Object)
                    continue;
                var id = ReadString(exercise, "exerciseId");
                if (!string.IsNullOrWhiteSpace(id))
                    result.TryAdd(id, exercise.Clone());
            }
        }
        catch (JsonException)
        {
            // Invalid feedback is missing information, never evidence for a volume change.
        }
        catch (InvalidOperationException)
        {
            // Unexpected JSON shapes are treated as missing feedback.
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record PrescribedExercise(
        string ExerciseId,
        int Sets,
        IReadOnlyList<string> EffectiveMuscles);
}

public sealed record TrainingMuscleFeedbackRecord(
    DateOnly Date,
    string Outcome,
    string? ExerciseFeedbackJson,
    string? PrescriptionJson);
