using Trazo.Training;

namespace Trazo.Application.Planning;

public sealed record GeneratedSession(
    int WeekNumber, DateOnly? Date, DayOfWeek Day, string Title, int EstimatedDurationMinutes, string PrescriptionJson);

public sealed record MesocycleGeneration(
    IReadOnlyList<GeneratedSession> Sessions, IReadOnlyList<string> Errors, TrainingProgramQualityReport? Quality,
    string EngineVersion = "")
{
    public bool Succeeded => Errors.Count == 0 && Sessions.Count > 0;
}

/// <summary>
/// Generates ONE block of a plan with the engine. Pure and deterministic: the same profile, seed, block, catalog and
/// history always give the same sessions. Week numbers and dates are absolute in the plan.
/// </summary>
public static class MesocycleGenerator
{
    public static MesocycleGeneration Generate(
        PlanProfile profile,
        int seed,
        MesocycleSlot slot,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlyList<string>? previousBlockExerciseIds = null,
        bool continuesPreviousBlock = false,
        IReadOnlyList<TrainingLoadReference>? loadReferences = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(catalog);

        var excluded = profile.ExcludedExerciseIds
            .Concat(LimitationRules.ExcludedExerciseIds(profile.Limitations, catalog))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var result = TrainingProgramGenerator.Generate(new TrainingProgramGenerationRequest(
            profile.Goal, profile.Experience, PlanSchedule.WeekStart(profile.StartDate, slot.StartWeek), slot.Weeks,
            profile.TrainingDays, profile.MaxSessionMinutes, profile.AvailableEquipment, excluded,
            MuscleVolumeTargets: null, IsDeload: slot.IsDeload, Split: profile.Split,
            RotationIndex: Math.Abs(seed % 1000) + slot.Sequence,
            LoadReferences: loadReferences,
            PreviousBlockExerciseIds: previousBlockExerciseIds,
            ContinuesPreviousBlock: continuesPreviousBlock,
            SelectionSeed: seed,
            IsSequence: profile.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence,
            RotationStartIndex: slot.StartWeek - 1), catalog);
        if (!result.Succeeded) return new([], result.Errors, null);

        // A sequence plan has no dates: its sessions keep the order of the rotation (the engine's days only lay it out).
        var sequence = profile.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence;
        var generated = result.Prescription!.Sessions
            .Select(session =>
            {
                var week = slot.StartWeek + session.WeekNumber - 1;
                return new GeneratedSession(
                    week, sequence ? null : PlanSchedule.DateFor(profile.StartDate, week, session.Day), session.Day, session.Title,
                    session.EstimatedDurationMinutes, PrescriptionJson.Serialize(
                        slot.PhaseType == IntensificationPhase.PhaseType ? IntensificationPhase.Apply(session.Exercises) : session.Exercises));
            });
        var sessions = (sequence
                ? generated.OrderBy(session => session.WeekNumber).ThenBy(session => profile.TrainingDays.ToList().IndexOf(session.Day))
                : generated.OrderBy(session => session.Date).ThenBy(session => session.Title, StringComparer.Ordinal))
            .ToArray();
        return new(sessions, [], result.Prescription.QualityReport, result.Prescription.EngineVersion);
    }
}
