namespace Trazo.Training;

/// <summary>
/// Decides which adaptation proposals the system applies BY ITSELF to the future sessions of a plan,
/// without a professional's approval (product decision), and which stay pending for human review.
///
/// What is applied automatically (all reversible, small and bounded; every application is stored as an
/// approved proposal with no reviewer, so it is auditable and visible to the professional):
///  - reduce one set in the exercises with repeated fatigue signals (next 3 sessions each);
///  - +1 RIR in the next 3 sessions when low recovery repeats and no exercise can be blamed;
///  - give back one previously removed set after clean exposures (never above the original dose);
///  - a conservative deload of the next 7 days when stress signals converge (once per 28 days);
///  - pause load progression text for the next sessions;
///  - +1 / -1 set for a muscle from the post-exercise answers (stimulus, fatigue), one session per change.
/// What is NEVER applied automatically: pain / safety reviews (they pause progression of that exercise
/// and wait for a human) and session reorganisation.
/// Cooldowns stop the system from stacking changes: if the same kind of change was applied recently
/// the proposal stays pending. Cooldowns and window sizes are internal product values, not guidelines.
/// </summary>
public static class TrainingAutoAdaptationPolicy
{
    public const int MaximumFutureSessions = 3;
    public const int DeloadWindowDays = 7;

    public static TrainingAutoAdaptationDecision Decide(
        string proposalAction,
        IReadOnlyList<string> targetExerciseIds,
        DateTimeOffset now,
        IReadOnlyList<TrainingAutoAdaptationRecord> recentlyApplied)
    {
        ArgumentNullException.ThrowIfNull(targetExerciseIds);
        ArgumentNullException.ThrowIfNull(recentlyApplied);

        (TrainingAutoAdaptationKind kind, int cooldownDays) = proposalAction switch
        {
            "review_and_reduce_training_stress" when targetExerciseIds.Count > 0 => (TrainingAutoAdaptationKind.ReduceSets, 14),
            "review_and_reduce_training_stress" => (TrainingAutoAdaptationKind.RaiseReserve, 14),
            "restore_volume" => (TrainingAutoAdaptationKind.RestoreSets, 14),
            "propose_deload" => (TrainingAutoAdaptationKind.Deload, 28),
            "hold_progression_and_review" => (TrainingAutoAdaptationKind.HoldProgression, 7),
            "add_muscle_set" => (TrainingAutoAdaptationKind.AddMuscleSet, 14),
            "reduce_muscle_set" => (TrainingAutoAdaptationKind.ReduceMuscleSet, 14),
            _ => (TrainingAutoAdaptationKind.None, 0)
        };
        if (kind == TrainingAutoAdaptationKind.None)
            return new(kind, [], "Esta señal requiere revisión de una persona; no se aplica nada automáticamente.");

        if (recentlyApplied.Any(item => item.Kind == kind && now - item.AppliedAt < TimeSpan.FromDays(cooldownDays)))
            return new(TrainingAutoAdaptationKind.None, [],
                $"Ya se aplicó un ajuste del mismo tipo en los últimos {cooldownDays} días; se espera antes de encadenar otro.");

        // A deload already in progress absorbs every other automatic load/volume adjustment.
        // This prevents a muscle-level +1/-1 set proposal or a restoration from counteracting
        // the deload that was just applied to the same future sessions.
        var isLoadOrVolumeAdjustment = kind is
            TrainingAutoAdaptationKind.ReduceSets or
            TrainingAutoAdaptationKind.RaiseReserve or
            TrainingAutoAdaptationKind.RestoreSets or
            TrainingAutoAdaptationKind.HoldProgression or
            TrainingAutoAdaptationKind.AddMuscleSet or
            TrainingAutoAdaptationKind.ReduceMuscleSet;
        if (isLoadOrVolumeAdjustment &&
            recentlyApplied.Any(item => item.Kind == TrainingAutoAdaptationKind.Deload &&
                                        now - item.AppliedAt < TimeSpan.FromDays(DeloadWindowDays)))
            return new(TrainingAutoAdaptationKind.None, [],
                "Hay una descarga en curso; se espera a que termine antes de encadenar otro ajuste de carga o volumen.");

        return new(kind, targetExerciseIds, null);
    }

    public static string DescribeKind(TrainingAutoAdaptationKind kind) => kind switch
    {
        TrainingAutoAdaptationKind.ReduceSets => "reduction_sets",
        TrainingAutoAdaptationKind.RaiseReserve => "raise_reserve",
        TrainingAutoAdaptationKind.RestoreSets => "restore_sets",
        TrainingAutoAdaptationKind.Deload => "deload",
        TrainingAutoAdaptationKind.HoldProgression => "hold_progression",
        TrainingAutoAdaptationKind.AddMuscleSet => "add_muscle_set",
        TrainingAutoAdaptationKind.ReduceMuscleSet => "reduce_muscle_set",
        _ => "none"
    };
}

public enum TrainingAutoAdaptationKind { None, ReduceSets, RaiseReserve, RestoreSets, Deload, HoldProgression, AddMuscleSet, ReduceMuscleSet }

public sealed record TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind Kind, DateTimeOffset AppliedAt);

public sealed record TrainingAutoAdaptationDecision(
    TrainingAutoAdaptationKind Kind, IReadOnlyList<string> ExerciseIds, string? SkipReason)
{
    public bool ShouldApply => Kind != TrainingAutoAdaptationKind.None;
}
