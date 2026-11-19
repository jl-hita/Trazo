using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>What is stored on a mesocycle to explain, later, how it was generated. See <see cref="MesocycleGenerationTrace.Capture"/>.</summary>
public sealed record GenerationTrace(
    string SnapshotJson, int SnapshotSchemaVersion, string EngineVersion, string CatalogFingerprint, DateTimeOffset GeneratedAt);

/// <summary>
/// The traceability of one generated block: the context the generator was given, the engine version that produced it, and a
/// fingerprint of the exercise catalog it saw. Built from the INPUTS of the generation at the moment it happens (the plan may change
/// later, and a deferred block is generated with the plan as it is by then), then stored once and never rewritten.
/// It records only what conditions the generation; it does not copy the plan, the catalog or the sessions.
/// </summary>
public static class MesocycleGenerationTrace
{
    /// <summary>Layout version of the snapshot JSON. Bump it when a field is added, removed or changes meaning.</summary>
    public const int SnapshotSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <param name="generation">The generation that was ACTUALLY used (after any fallback); its engine version is the one recorded.</param>
    /// <param name="learnedTargets">The learned targets the generation really used, or null/empty when it ran without learning.</param>
    public static GenerationTrace Capture(
        PlanProfile profile, int seed, MesocycleSlot slot, IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        MesocycleGeneration generation, int rotationPosition, bool continuesPreviousBlock,
        IReadOnlyList<string>? previousBlockExerciseIds, int loadReferenceCount,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal>? learnedTargets, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(generation);
        if (string.IsNullOrWhiteSpace(generation.EngineVersion))
            throw new InvalidOperationException("A generated block always carries the version of the engine that generated it.");

        var learned = learnedTargets is { Count: > 0 };
        var snapshot = new
        {
            schemaVersion = SnapshotSchemaVersion,
            profile = new
            {
                goal = profile.Goal.ToString(),
                experience = profile.Experience.ToString(),
                split = profile.Split.ToString(),
                schedulingMode = profile.SchedulingMode.ToString(),
                sessionsPerPeriod = profile.SessionsPerPeriod,
                // Sorted by weekday (Monday first) so the same days always read the same.
                trainingDays = profile.TrainingDays.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString()).ToArray(),
                maxSessionMinutes = profile.MaxSessionMinutes,
                availableEquipment = Sorted(profile.AvailableEquipment),
                excludedExerciseIds = Sorted(profile.ExcludedExerciseIds),
                limitations = Sorted(profile.Limitations),
                musclePriorities = (profile.MusclePriorities ?? new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>())
                    .OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(), p => p.Value.ToString()),
                includeDeloadPhases = profile.IncludeDeloadPhases,
                deloadEveryTrainingWeeks = profile.DeloadEveryTrainingWeeks,
                startDate = profile.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                totalWeeks = profile.TotalWeeks
            },
            seed,
            block = new
            {
                sequence = slot.Sequence, phaseSequenceNumber = slot.PhaseSequenceNumber, phaseType = slot.PhaseType,
                isDeload = slot.IsDeload, startWeek = slot.StartWeek, weeks = slot.Weeks
            },
            inputs = new
            {
                rotationPosition,
                continuesPreviousBlock,
                previousBlockExerciseIds = Sorted(previousBlockExerciseIds ?? []),
                // The load references come from the user's history; only how many were used is kept, not the values.
                loadReferenceCount
            },
            learning = new
            {
                applied = learned,
                policyVersion = learned ? LongitudinalPolicy.Version : null,
                learnedTargets = learned
                    ? learnedTargets!.OrderBy(t => t.Key).ToDictionary(t => t.Key.ToString(), t => t.Value)
                    : new Dictionary<string, decimal>()
            },
            catalog = new { exercises = catalog.Count },
            engineVersion = generation.EngineVersion
        };
        return new GenerationTrace(
            JsonSerializer.Serialize(snapshot, Json), SnapshotSchemaVersion, generation.EngineVersion, CatalogFingerprint.Compute(catalog), now);
    }

    private static string[] Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// A deterministic fingerprint of the exercise catalog as the generator reads it. The catalog has a stable id per exercise but no
/// version of its own as a whole, so the fingerprint is the SHA-256 of every exercise's id, per-exercise catalog version and every field
/// the generator selects on, in id order. The same content always gives the same value, whatever order the rows were read in; any change
/// to an exercise the generator can see (added, removed, deactivated, re-tagged) gives a different one. It says THAT the catalog
/// changed, not what changed: the catalog itself is not copied.
/// </summary>
public static class CatalogFingerprint
{
    public static string Compute(IEnumerable<TrainingExerciseCandidate> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var text = new StringBuilder();
        foreach (var c in catalog.OrderBy(c => c.ExerciseId, StringComparer.Ordinal))
        {
            text.Append(c.ExerciseId).Append('|').Append(c.CatalogVersion).Append('|')
                .Append(c.MovementPattern).Append('|').Append(c.MinimumExperience).Append('|')
                .Append(c.SelectionPriority.ToString(CultureInfo.InvariantCulture)).Append('|').Append(c.IsReviewed ? '1' : '0').Append('|')
                .Append(Join(c.RequiredEquipment)).Append('|').Append(Join(c.SuitableGoals.Select(g => g.ToString()))).Append('|')
                .Append(Join(c.PrimaryMuscleGroups.Select(m => m.ToString()))).Append('|')
                .Append(Join(c.SecondaryMuscleGroups.Select(m => m.ToString()))).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static string Join(IEnumerable<string> values) => string.Join(',', values.OrderBy(v => v, StringComparer.Ordinal));
}
