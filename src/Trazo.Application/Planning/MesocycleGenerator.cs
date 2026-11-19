using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// One generated session in program order. <c>WeekNumber</c> is the plan period number (a calendar week in calendar mode, a
/// sequence period in sequence mode); <c>Date</c> and <c>Day</c> are populated only for calendar plans.
/// </summary>
public sealed record GeneratedSession(
    int WeekNumber, DateOnly? Date, DayOfWeek? Day, string Title, int EstimatedDurationMinutes, string PrescriptionJson);

/// <summary>
/// Result of generating one block. A successful result contains the ordered sessions and the engine's non-blocking quality report;
/// failures contain errors and no sessions. The engine version travels with the result so persisted prescriptions can be traced
/// to the rules that produced them.
/// </summary>
public sealed record MesocycleGeneration(
    IReadOnlyList<GeneratedSession> Sessions, IReadOnlyList<string> Errors, TrainingProgramQualityReport? Quality,
    string EngineVersion = "")
{
    public bool Succeeded => Errors.Count == 0 && Sessions.Count > 0;
}

/// <summary>
/// Generates ONE block of a plan with the shared pure engine. Given the same profile, seed, block, catalog, history and rotation
/// position it produces the same sessions. Calendar plans lay the unit over fixed training weekdays; sequence plans lay a continuous
/// session rotation over consecutive periods and resume at the persisted session position. The period number (and calendar date,
/// where applicable) is absolute within the plan; a period boundary never resets a sequence rotation.
/// </summary>
public static class MesocycleGenerator
{
    /// <summary>
    /// The weekly target of each configurable muscle for this profile before any learning, with the limits a learned change must respect.
    /// It uses exactly the same inputs as <see cref="Generate"/>, so the longitudinal layer reads the numbers the generator will use.
    /// </summary>
    public static IReadOnlyList<TrainingMuscleBaseline> Baselines(PlanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var sequence = profile.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence;
        return TrainingMusclePriorityPlanner.Baselines(
            profile.Goal, profile.Experience, profile.MusclePriorities,
            sequence ? profile.SessionsPerPeriod : profile.TrainingDays.Count, profile.MaxSessionMinutes);
    }

    /// <param name="learnedTargets">Weekly targets from the longitudinal layer (see <c>Longitudinal.LongitudinalVolumePlanner</c>); null or empty leaves the block exactly as it was without learning.</param>
    /// <param name="rotationPosition">Sequence plans only: how many sessions the plan already has, that is, where in the rotation the
    /// block begins. It comes from what is persisted (the order of the existing sessions), never from the clock.</param>
    public static MesocycleGeneration Generate(
        PlanProfile profile,
        int seed,
        MesocycleSlot slot,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlyList<string>? previousBlockExerciseIds = null,
        bool continuesPreviousBlock = false,
        IReadOnlyList<TrainingLoadReference>? loadReferences = null,
        int rotationPosition = 0,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal>? learnedTargets = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(catalog);

        var excluded = profile.ExcludedExerciseIds
            .Concat(LimitationRules.ExcludedExerciseIds(profile.Limitations, catalog))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var sequence = profile.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence;
        // The user's muscle priorities become the volume targets the allocator already understands (none = the default dose).
        var priorities = TrainingMusclePriorityPlanner.Build(
            profile.Goal, profile.Experience, profile.MusclePriorities,
            sequence ? profile.SessionsPerPeriod : profile.TrainingDays.Count, profile.MaxSessionMinutes, learnedTargets);
        if (!priorities.Succeeded) return new([], priorities.Errors, null);
        var result = TrainingProgramGenerator.Generate(new TrainingProgramGenerationRequest(
            profile.Goal, profile.Experience, PlanSchedule.WeekStart(profile.StartDate, slot.StartWeek), slot.Weeks,
            sequence ? [] : profile.TrainingDays, profile.MaxSessionMinutes, profile.AvailableEquipment, excluded,
            MuscleVolumeTargets: priorities.Targets.Count > 0 ? priorities.Targets : null, IsDeload: slot.IsDeload, Split: profile.Split,
            RotationIndex: Math.Abs(seed % 1000) + slot.Sequence,
            LoadReferences: loadReferences,
            PreviousBlockExerciseIds: previousBlockExerciseIds,
            ContinuesPreviousBlock: continuesPreviousBlock,
            SelectionSeed: seed,
            Sequence: sequence ? new TrainingSessionSequence(profile.SessionsPerPeriod, rotationPosition) : null), catalog);
        if (!result.Succeeded) return new([], result.Errors, null);

        // The engine returns the sessions of every period in program order. A sequence plan keeps that order and has no dates;
        // a calendar plan places each session on its training day of the week.
        var generated = result.Prescription!.Sessions.Select(session =>
        {
            var week = slot.StartWeek + session.WeekNumber - 1;
            var exercises = slot.PhaseType == IntensificationPhase.PhaseType ? IntensificationPhase.Apply(session.Exercises) : session.Exercises;
            return new GeneratedSession(
                week, sequence ? null : PlanSchedule.DateFor(profile.StartDate, week, session.Day), sequence ? null : session.Day,
                session.Title, session.EstimatedDurationMinutes, PrescriptionJson.Serialize(exercises));
        });
        var sessions = (sequence
                ? generated
                : generated.OrderBy(session => session.Date).ThenBy(session => session.Title, StringComparer.Ordinal))
            .ToArray();
        return new(sessions, [], result.Prescription.QualityReport, result.Prescription.EngineVersion);
    }
}
