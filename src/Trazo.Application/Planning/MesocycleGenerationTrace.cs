using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>Where one load reference given to the generator came from: the exposure workout log and top set, and the values used.</summary>
public sealed record LoadReferenceSource(
    string ExerciseId, Guid WorkoutLogId, int SetNumber, DateOnly PerformedOn, decimal WeightKg, int Repetitions, decimal? Rir, int CompletedSets);

/// <summary>
/// The load references the generator received, with their provenance and the criteria that selected them. <see cref="References"/> is
/// exactly what the generator consumes; the rest only explains it.
/// </summary>
public sealed record LoadReferenceSelection(
    IReadOnlyList<TrainingLoadReference> References, IReadOnlyList<LoadReferenceSource> Sources, bool SuppressedByPain,
    IReadOnlyList<string> WithheldForPain, int LogsConsidered, int LogsLimit, int PainSessionWindow, int PainExposureWindow, int LateRegistrationDays)
{
    /// <summary>No history: nothing was available to reference.</summary>
    public static LoadReferenceSelection None { get; } = new([], [], false, [], 0, 0, 0, 0, 0);
}

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
        IReadOnlyList<string>? previousBlockExerciseIds, LoadReferenceSelection loadReferences,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal>? learnedTargets, DateTimeOffset now, TechniqueAllowance? techniques = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(loadReferences);
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
                loadReferences = LoadReferencesOf(loadReferences)
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
            engineVersion = generation.EngineVersion,
            // Whether the generator could add techniques, and the exercises kept out of them for pain.
            techniques = new
            {
                allowed = techniques is not null,
                painfulExercises = techniques is null ? [] : techniques.PainfulExercises.Order(StringComparer.Ordinal).ToArray()
            }
        };
        return new GenerationTrace(
            JsonSerializer.Serialize(snapshot, Json), SnapshotSchemaVersion, generation.EngineVersion, CatalogFingerprint.Compute(catalog), now);
    }

    /// <summary>
    /// The load references the generator received: for each one the exercise, the workout log (and set) it was read from, the date, and the
    /// effective values given to the generator, so it stays interpretable if the log is later edited or deleted. No notes, no other sets,
    /// no feedback: only what the generator consumed plus the criteria that chose it.
    /// </summary>
    private static object LoadReferencesOf(LoadReferenceSelection selection) => new
    {
        suppressedByPain = selection.SuppressedByPain,
        withheldForPain = selection.WithheldForPain.Order(StringComparer.Ordinal).ToArray(),
        criteria = new
        {
            rule = "latest eligible exposure per exercise; heaviest completed set (ties: lowest set number); late and deload exposures skipped",
            logsConsidered = selection.LogsConsidered, logsLimit = selection.LogsLimit,
            painSessionWindow = selection.PainSessionWindow, painExposureWindow = selection.PainExposureWindow,
            lateRegistrationDays = selection.LateRegistrationDays
        },
        items = selection.Sources.OrderBy(r => r.ExerciseId, StringComparer.Ordinal).Select(r => new
        {
            exerciseId = r.ExerciseId, workoutLogId = r.WorkoutLogId, setNumber = r.SetNumber,
            performedOn = r.PerformedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            weightKg = r.WeightKg, repetitions = r.Repetitions, rir = r.Rir, completedSets = r.CompletedSets
        }).ToArray()
    };

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
    private static readonly JsonSerializerOptions Canonical = new()
    {
        WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// The canonical form: a JSON object with explicit, typed fields, exercises in ordinal id order and every inner collection sorted.
    /// JSON quoting makes the representation unambiguous: no character in a value can be mistaken for a separator.
    /// </summary>
    public static string Canonicalize(IEnumerable<TrainingExerciseCandidate> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var exercises = catalog.OrderBy(c => c.ExerciseId, StringComparer.Ordinal).Select(c => new
        {
            id = c.ExerciseId,
            catalogVersion = c.CatalogVersion,
            movementPattern = c.MovementPattern.ToString(),
            minimumExperience = c.MinimumExperience.ToString(),
            selectionPriority = c.SelectionPriority,
            isReviewed = c.IsReviewed,
            requiredEquipment = Sorted(c.RequiredEquipment),
            suitableGoals = Sorted(c.SuitableGoals.Select(g => g.ToString())),
            primaryMuscles = Sorted(c.PrimaryMuscleGroups.Select(m => m.ToString())),
            secondaryMuscles = Sorted(c.SecondaryMuscleGroups.Select(m => m.ToString())),
            effectiveSecondaryMuscles = Sorted((c.EffectiveSecondaryMuscleGroups ?? []).Select(m => m.ToString())),
            emphasis = Sorted((c.Emphasis ?? []).Select(e => e.ToString())),
            limitationAreas = Sorted(c.LimitationAreas ?? []),
            allowedTechniques = Sorted((c.AllowedTechniques ?? []).Select(t => t.ToString()))
        }).ToArray();
        return JsonSerializer.Serialize(new { fingerprintVersion = 3, exercises }, Canonical);
    }

    public static string Compute(IEnumerable<TrainingExerciseCandidate> catalog) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(catalog)))).ToLowerInvariant();

    private static string[] Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal).ToArray();
}
