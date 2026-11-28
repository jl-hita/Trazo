namespace Trazo.Training;

/// <summary>
/// Exercise-specific volume review engine. It distinguishes repeated fatigue signals from
/// time/equipment/adherence issues and pain. It returns proposals only; a professional must approve
/// any change to future prescriptions.
/// </summary>
public static class TrainingExerciseVolumeAdapter
{
    private const int AnalysisWindow = 3;
    private const int RequiredFatigueSignals = 2;
    private const decimal MeaningfulRirShortfall = 1m;
    // Internal product limits: no exercise is cut below two sets, and none loses more than one set in a block.
    private const int MinimumSets = 2;
    /// <summary>Reasons for leaving a set out that say nothing about fatigue.</summary>
    private static readonly string[] OutsideSkipReasons = ["time", "equipment", "motivation", "illness"];

    public static IReadOnlyList<TrainingExerciseVolumeRecommendation> Evaluate(
        IReadOnlyList<TrainingExerciseVolumeExposure> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var recommendations = new List<TrainingExerciseVolumeRecommendation>();

        foreach (var group in history
            .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseId))
            .GroupBy(item => item.ExerciseId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var recent = group.TakeLast(AnalysisWindow).ToArray();
            if (recent.Length == 0)
                continue;

            // Pain is a safety signal and must never be converted into an ordinary volume tweak.
            if (recent.Any(item => item.PainReported))
            {
                recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.PauseAndReview,
                    "Se ha registrado dolor en este ejercicio. No ajustes el volumen como si fuera fatiga; evita progresarlo y solicita revisión profesional.",
                    null, recent.Length));
                continue;
            }

            if (recent.Length < AnalysisWindow)
            {
                recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.NoChange,
                    "Hay menos de tres exposiciones válidas recientes; no se propone reducir volumen con una muestra insuficiente.",
                    null, recent.Length));
                continue;
            }

            // A missed set due to time, equipment, illness or motivation is not evidence that
            // the exercise itself needs less volume. Only repeated fatigue-coded signals count.
            // Count at most one signal per exposure even when both skipped sets and RIR
            // indicate fatigue; otherwise one difficult session could masquerade as repetition.
            // A reserve below the target is not fatigue on its own: many people write it that way every time. It counts when
            // something else agrees: less done with the same load, insufficient recovery, or sets left undone.
            bool HasFatigueSignal(TrainingExerciseVolumeExposure item) =>
                item.SessionCompleted &&
                ((string.Equals(item.SkippedSetReason, "fatigue", StringComparison.OrdinalIgnoreCase) &&
                  item.CompletedWorkingSets < item.PlannedWorkingSets) ||
                 (item.TargetRir.HasValue && item.ActualAverageRir.HasValue &&
                  item.ActualAverageRir.Value <= item.TargetRir.Value - MeaningfulRirShortfall &&
                  (item.PerformanceFell || item.RecoveryInsufficient || LeftSetsUndone(item))));
            static bool LeftSetsUndone(TrainingExerciseVolumeExposure item) =>
                item.CompletedWorkingSets < item.PlannedWorkingSets &&
                !OutsideSkipReasons.Contains(item.SkippedSetReason, StringComparer.OrdinalIgnoreCase);

            var repeatedFatigueSignals = recent.Count(HasFatigueSignal);
            // Require the latest exposure to still show the problem; old fatigue that has
            // resolved should not keep reducing the next mesocycle's volume.
            if (repeatedFatigueSignals >= RequiredFatigueSignals && HasFatigueSignal(recent[^1]))
            {
                var latest = recent[^1];
                if (latest.PlannedWorkingSets <= MinimumSets)
                {
                    recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.ReviewWithoutReduction,
                        "Hay señales repetidas de fatiga, pero el ejercicio ya está en dos series o menos. Revisa técnica, recuperación y distribución semanal; no reduzcas más volumen automáticamente.",
                        null, recent.Length));
                    continue;
                }

                if (latest.ReducedThisBlock || latest.OriginalPlannedSets > latest.PlannedWorkingSets)
                {
                    recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.NoChange,
                        "Mantén la prescripción.", null, recent.Length));
                    continue;
                }

                recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.ProposeReduceOneSet,
                    "En al menos dos de las tres exposiciones recientes se repite una señal específica de fatiga. Se propone revisar una reducción de una serie solo en este ejercicio; requiere aprobación profesional.",
                    latest.PlannedWorkingSets - 1, recent.Length));
                continue;
            }

            // Reversal: a previously approved reduction is not permanent. When the three latest
            // exposures were all completed at the reduced dose with no fatigue signal and the effort
            // reserve was met or short by no more than the shortfall that counts, propose giving back ONE set (never above the original dose).
            // Internal product rule; requires professional approval like any volume change.
            var latestExposure = recent[^1];
            bool HasMetTargetEffortReserve(TrainingExerciseVolumeExposure item) =>
                item.TargetRir.HasValue && item.ActualAverageRir.HasValue &&
                item.ActualAverageRir.Value >= item.TargetRir.Value - MeaningfulRirShortfall;

            if (recent.All(item => item.OriginalPlannedSets is { } original && original > item.PlannedWorkingSets &&
                                   item.SessionCompleted && !HasFatigueSignal(item) &&
                                   HasMetTargetEffortReserve(item) &&
                                   item.CompletedWorkingSets >= item.PlannedWorkingSets) &&
                latestExposure.OriginalPlannedSets > latestExposure.PlannedWorkingSets)
            {
                recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.ProposeRestoreOneSet,
                    "Las tres últimas exposiciones se completaron con el volumen reducido y sin señales de fatiga. Se propone recuperar una serie (sin superar la dosis original); requiere aprobación profesional.",
                    latestExposure.PlannedWorkingSets + 1, recent.Length));
                continue;
            }

            recommendations.Add(new(group.Key, TrainingExerciseVolumeAction.NoChange,
                "No hay señales específicas y repetidas suficientes para reducir el volumen de este ejercicio. Mantén la prescripción.",
                null, recent.Length));
        }

        return recommendations;
    }
}

public sealed record TrainingExerciseVolumeExposure(
    string ExerciseId,
    int PlannedWorkingSets,
    int CompletedWorkingSets,
    string? SkippedSetReason = null,
    bool SessionCompleted = true,
    bool PainReported = false,
    decimal? TargetRir = null,
    decimal? ActualAverageRir = null,
    int? OriginalPlannedSets = null,
    /// <summary>Less was done (repetitions plus reserve) than an earlier time with the same load and repetition range.</summary>
    bool PerformanceFell = false,
    bool RecoveryInsufficient = false,
    /// <summary>This exercise was already done with a set taken away in the same block.</summary>
    bool ReducedThisBlock = false);

public sealed record TrainingExerciseVolumeRecommendation(
    string ExerciseId,
    TrainingExerciseVolumeAction Action,
    string Explanation,
    int? SuggestedWorkingSets,
    int ExposuresAnalyzed);

public enum TrainingExerciseVolumeAction
{
    NoChange,
    ProposeReduceOneSet,
    ProposeRestoreOneSet,
    ReviewWithoutReduction,
    PauseAndReview
}
