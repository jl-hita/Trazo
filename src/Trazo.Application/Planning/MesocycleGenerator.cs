using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// One generated session. <paramref name="Date"/> and <paramref name="Day"/> exist only in a calendar plan; a sequence plan has
/// neither, and its sessions are identified by their order.
/// </summary>
public sealed record GeneratedSession(
    int WeekNumber, DateOnly? Date, DayOfWeek? Day, string Title, int EstimatedDurationMinutes, string PrescriptionJson);

public sealed record MesocycleGeneration(
    IReadOnlyList<GeneratedSession> Sessions, IReadOnlyList<string> Errors, TrainingProgramQualityReport? Quality,
    string EngineVersion = "")
{
    public bool Succeeded => Errors.Count == 0 && Sessions.Count > 0;
}

/// <summary>
/// Generates ONE block of a plan with the engine. Pure and deterministic: the same profile, seed, block, catalog, history and
/// rotation position always give the same sessions. The engine is the same for both scheduling modes; this adapter only says
/// what the block is laid over: the training days of each week (calendar) or the continuation of the plan's rotation (sequence).
/// Period numbers (and, in a calendar plan, dates) are absolute in the plan.
/// </summary>
public static class MesocycleGenerator
{
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
        int rotationPosition = 0)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(catalog);

        var excluded = profile.ExcludedExerciseIds
            .Concat(LimitationRules.ExcludedExerciseIds(profile.Limitations, catalog))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var sequence = profile.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence;
        var result = TrainingProgramGenerator.Generate(new TrainingProgramGenerationRequest(
            profile.Goal, profile.Experience, PlanSchedule.WeekStart(profile.StartDate, slot.StartWeek), slot.Weeks,
            sequence ? [] : profile.TrainingDays, profile.MaxSessionMinutes, profile.AvailableEquipment, excluded,
            MuscleVolumeTargets: null, IsDeload: slot.IsDeload, Split: profile.Split,
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
