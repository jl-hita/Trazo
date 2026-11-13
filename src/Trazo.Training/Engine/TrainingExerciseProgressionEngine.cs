namespace Trazo.Training;

/// <summary>
/// Exercise-level double-progression engine. It produces a suggestion for the next exposure;
/// it never edits a prescription, changes completed history, or applies a load automatically.
/// </summary>
public static class TrainingExerciseProgressionEngine
{
    private const int RequiredConsecutiveTopRangeExposures = 2;
    private const decimal DefaultMaximumLoadIncreasePercent = 5m;

    public static TrainingExerciseProgressionRecommendation Evaluate(
        TrainingExerciseProgressionPrescription prescription,
        IReadOnlyList<TrainingExerciseExposure> history)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        ArgumentNullException.ThrowIfNull(history);

        var validation = ValidatePrescription(prescription);
        if (validation is not null)
            return Recommendation(TrainingExerciseProgressionAction.InvalidPrescription, validation);

        // A pain report is exercise/session safety context, not a performance score.
        // It takes precedence even when progression is disabled (e.g. during a deload).
        // Never let a later pain-free record erase a recent unresolved pain signal.
        if (history.TakeLast(3).Any(x => x.PainReported))
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "Hay una señal reciente de dolor. No progreses este ejercicio hasta revisar el problema y confirmar que es seguro retomarlo.");

        if (prescription.ProgressionRule != TrainingProgressionRule.DoubleProgression)
            return Recommendation(TrainingExerciseProgressionAction.NotApplicable,
                "Esta regla de progresión no tiene todavía una semántica validada para recomendaciones automáticas. Mantén la prescripción y solicita revisión profesional.");

        var exposures = history.TakeLast(3).ToArray();

        if (exposures.Length == 0)
            return Recommendation(TrainingExerciseProgressionAction.InsufficientData,
                "Todavía no hay una sesión con series registradas para este ejercicio.");

        // Repeated failure at the same load is the signal that the prescribed weight was too high.
        // It is checked before the generic "hold" rules because those exposures are usually incomplete.
        var reduction = TryRecommendLoadReduction(prescription, exposures);
        if (reduction is not null) return reduction;

        // An incomplete latest exposure is meaningful negative context. Do not skip it
        // and accidentally progress from an older, better session.
        if (!exposures[^1].SessionCompleted)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "La última exposición no se completó. Mantén la carga y revisa el motivo antes de progresar.");

        if (!exposures[^1].ProgressionEligible)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "La última exposición pertenecía a una fase de descarga o no progresable. Mantén la carga y confirma el rendimiento en una sesión normal antes de progresar.");

        var latest = AnalyzeExposure(prescription, exposures[^1]);
        if (!latest.IsUsable)
            return Recommendation(latest.Invalid ? TrainingExerciseProgressionAction.InvalidData : TrainingExerciseProgressionAction.HoldAndReview,
                latest.Explanation);

        if (exposures[^1].RecoveryInsufficient)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "La recuperación registrada fue insuficiente. Mantén la carga y revisa el contexto antes de progresar.");

        if (latest.BelowMinimumRepetitions)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "No se alcanzó el mínimo del rango prescrito. Mantén la carga y revisa técnica, descanso, fatiga y contexto; no compenses aumentando el esfuerzo.");

        if (latest.BelowTargetRir)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "El esfuerzo superó el objetivo de RIR. Mantén la carga; no aumentes peso ni repeticiones hasta recuperar el margen prescrito.");

        if (!latest.AtTopOfRange && HasStagnated(prescription, exposures))
            return Recommendation(TrainingExerciseProgressionAction.StagnationReview,
                "El rendimiento no mejora en tres exposiciones consecutivas. Antes de cambiar la carga, revisa adherencia, técnica, descansos, recuperación y si el rango/RIR siguen siendo adecuados con tu profesional.");

        if (!latest.AtTopOfRange)
        {
            var nextRepetitions = latest.Sets
                .Select(set => Math.Min(prescription.MaxRepetitions, set.Repetitions!.Value + 1))
                .ToArray();
            return new(
                TrainingExerciseProgressionAction.AddRepetitions,
                "Mantén la carga y añade como máximo una repetición por serie en la próxima exposición, sin superar el rango prescrito ni perder el RIR objetivo.",
                latest.LoadKg,
                null,
                nextRepetitions,
                null);
        }

        // A single good session is not enough to justify a load jump. Requiring two
        // consecutive top-of-range exposures reduces reactions to day-to-day noise.
        var consecutiveTopRange = 1;
        for (var index = exposures.Length - 2; index >= 0; index--)
        {
            var previous = AnalyzeExposure(prescription, exposures[index]);
            if (!exposures[index].ProgressionEligible || !exposures[index].SessionCompleted ||
                !previous.IsUsable || previous.BelowTargetRir || previous.BelowMinimumRepetitions ||
                !previous.AtTopOfRange || previous.LoadKg != latest.LoadKg ||
                exposures[index].RecoveryInsufficient || exposures[index].PainReported)
                break;
            consecutiveTopRange++;
        }

        if (consecutiveTopRange < RequiredConsecutiveTopRangeExposures)
            return Recommendation(TrainingExerciseProgressionAction.ConfirmTopOfRange,
                "Has alcanzado el máximo del rango. Repite la carga y confirma el resultado en otra sesión antes de subir peso.");

        var maximumIncreasePercent = prescription.MaximumLoadIncreasePercent ?? DefaultMaximumLoadIncreasePercent;
        var maximumIncrease = decimal.Round(
            latest.LoadKg * maximumIncreasePercent / 100m, 2, MidpointRounding.AwayFromZero);
        if (prescription.SmallestLoadIncrementKg is not > 0)
            return new(
                TrainingExerciseProgressionAction.IncreaseLoad,
                $"Dos exposiciones consecutivas cumplen el rango y el RIR. Sube solo el menor incremento disponible del material, sin superar el {maximumIncreasePercent:0.##} % de la carga actual; registra el nuevo peso para calibrar futuras recomendaciones.",
                null, null, Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
                maximumIncreasePercent);

        var increment = prescription.SmallestLoadIncrementKg.Value;
        if (increment > maximumIncrease)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                $"El incremento disponible ({increment:0.##} kg) supera el límite conservador configurado del {maximumIncreasePercent:0.##} % ({maximumIncrease:0.##} kg). Mantén la carga o ajusta el material; no fuerces un salto excesivo.");

        var suggestedLoad = decimal.Round(latest.LoadKg + increment, 2, MidpointRounding.AwayFromZero);
        return new(
            TrainingExerciseProgressionAction.IncreaseLoad,
            "Dos exposiciones consecutivas cumplen el rango y el RIR. Aumenta un único incremento mínimo del material y vuelve al extremo inferior del rango de repeticiones.",
            suggestedLoad,
            increment,
            Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
            maximumIncreasePercent);
    }

    // Internal product thresholds (not guideline figures): two consecutive failed exposures at the
    // same load; if the estimate does not lower the load, fall back to a 5 % reduction.
    private const int RequiredFailedExposuresForReduction = 2;
    private const decimal FallbackReductionPercent = 5m;

    /// <summary>
    /// Detects "the prescribed weight was too high": the last two exposures used the same load and in
    /// both the lifter either completed fewer sets than prescribed (for no external reason such as
    /// time, equipment, illness, motivation or pain) or fell below the minimum of the rep range.
    /// Session-level insufficient recovery and non-progressable (deload) exposures are excluded, since
    /// they would confound the cause. The result is a suggestion; the professional or the lifter
    /// decides. The new load comes from <see cref="TrainingLoadEstimator"/> (capped change).
    /// </summary>
    private static TrainingExerciseProgressionRecommendation? TryRecommendLoadReduction(
        TrainingExerciseProgressionPrescription prescription,
        IReadOnlyList<TrainingExerciseExposure> exposures)
    {
        if (exposures.Count < RequiredFailedExposuresForReduction) return null;
        var recent = exposures.TakeLast(RequiredFailedExposuresForReduction).ToArray();
        decimal? load = null;
        foreach (var exposure in recent)
        {
            if (!exposure.ProgressionEligible || exposure.PainReported || exposure.RecoveryInsufficient ||
                exposure.WorkingSets is null || exposure.WorkingSets.Count == 0)
                return null;
            var weights = exposure.WorkingSets.Select(set => set.WeightKg).Distinct().ToArray();
            if (weights.Length != 1 || weights[0] is not > 0) return null;
            if (load is not null && load != weights[0]) return null;
            load = weights[0];

            var completed = exposure.WorkingSets.Where(set => set.Completed).ToArray();
            var belowMinimum = completed.Any(set => set.Repetitions is < 1 || set.Repetitions < prescription.MinRepetitions);
            var incomplete = completed.Length < prescription.PlannedWorkingSets;
            var reason = exposure.SkippedSetReason;
            var reasonPointsToLoad = reason is null or "load_too_heavy" or "other";
            if (!(belowMinimum || (incomplete && reasonPointsToLoad))) return null;
        }

        var current = load!.Value;
        var increment = prescription.SmallestLoadIncrementKg ?? TrainingLoadEstimator.DefaultLoadIncrementKg;
        var lastCompleted = recent[^1].WorkingSets.Where(set => set.Completed).ToArray();
        var suggested = TrainingLoadEstimator.Suggest(
            lastCompleted, prescription.MinRepetitions, prescription.MaxRepetitions, prescription.TargetRir, increment)?.LoadKg;
        if (suggested is null || suggested >= current)
            suggested = TrainingLoadEstimator.Round(current * (1 - FallbackReductionPercent / 100m), increment);
        if (suggested >= current)
            suggested = Math.Max(increment, current - increment);
        return new(
            TrainingExerciseProgressionAction.ReduceLoad,
            $"En las dos últimas exposiciones con {current:0.##} kg no se completó el trabajo prescrito (series o mínimo de repeticiones). " +
            $"Es probable que la carga fuera demasiado alta: reduce a unos {suggested:0.##} kg en la próxima sesión y vuelve a progresar desde ahí. Revísalo con tu profesional si hay dolor o fatiga.",
            suggested, current - suggested, Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(), null);
    }

    private static bool HasStagnated(
        TrainingExerciseProgressionPrescription prescription,
        IReadOnlyList<TrainingExerciseExposure> exposures)
    {
        if (exposures.Count < 3) return false;
        var recent = exposures.TakeLast(3).ToArray();
        if (recent.Any(x => !x.SessionCompleted || !x.ProgressionEligible || x.RecoveryInsufficient || x.PainReported))
            return false;

        var analyses = recent.Select(x => AnalyzeExposure(prescription, x)).ToArray();
        if (analyses.Any(x => !x.IsUsable || x.BelowMinimumRepetitions || x.BelowTargetRir))
            return false;
        if (analyses.Select(x => x.LoadKg).Distinct().Count() != 1)
            return false;

        var totalRepetitions = analyses
            .Select(x => x.Sets.Sum(set => set.Repetitions!.Value))
            .ToArray();
        var noRepetitionProgress = totalRepetitions[1] <= totalRepetitions[0] &&
                                   totalRepetitions[2] <= totalRepetitions[1];

        // RIR estimates are noisy. A change greater than half an RIR is treated as a
        // possible effort improvement; smaller differences do not hide a three-session plateau.
        var allRirRecorded = analyses.All(x => x.Sets.All(set => set.Rir is not null));
        var noRirImprovement = !allRirRecorded ||
            analyses[^1].Sets.Average(set => set.Rir!.Value) <=
            analyses[0].Sets.Average(set => set.Rir!.Value) + 0.5m;

        return noRepetitionProgress && noRirImprovement;
    }

    private static ExposureAnalysis AnalyzeExposure(
        TrainingExerciseProgressionPrescription prescription,
        TrainingExerciseExposure exposure)
    {
        if (exposure.HistoricalPrescription is { } historical &&
            (historical.PlannedWorkingSets != prescription.PlannedWorkingSets ||
             historical.MinRepetitions != prescription.MinRepetitions ||
             historical.MaxRepetitions != prescription.MaxRepetitions ||
             historical.TargetRir != prescription.TargetRir))
            return ExposureAnalysis.Failure(
                "La prescripción de este ejercicio cambió entre exposiciones (series, rango o RIR). Espera a registrar una sesión con la prescripción actual antes de progresar.");

        if (exposure.WorkingSets is null || exposure.WorkingSets.Count != prescription.PlannedWorkingSets)
            return ExposureAnalysis.Failure("El número de series efectivas registradas no coincide con las prescritas. Si no pudiste completarlas, indica el motivo (por ejemplo, «peso demasiado alto») para que el sistema pueda ajustar la carga.");

        var sets = exposure.WorkingSets;
        if (sets.Any(x => !string.Equals(x.Kind, "working", StringComparison.Ordinal) ||
                          !x.Completed || x.WeightKg is null or <= 0 ||
                          x.Repetitions is null or < 1 or > 100 ||
                          x.Rir is < 0 or > 10))
            return ExposureAnalysis.Failure("Faltan datos válidos de carga, repeticiones o finalización en alguna serie efectiva.");

        var load = sets[0].WeightKg!.Value;
        // The current contract prescribes one working load per exercise. Mixed-load
        // back-off/drop sets are not silently averaged; they need an explicit future model.
        if (sets.Any(x => x.WeightKg != load))
            return ExposureAnalysis.Failure("Las series usan cargas distintas. No se calcula una progresión única hasta que la prescripción modele esas cargas.", invalid: true);

        if (sets.Any(x => x.Repetitions!.Value > prescription.MaxRepetitions))
            return ExposureAnalysis.Failure(
                "Se han registrado repeticiones por encima del máximo prescrito. Revisa el registro antes de calcular una progresión.",
                invalid: true);

        var belowMinimum = sets.Any(x => x.Repetitions!.Value < prescription.MinRepetitions);
        var belowRir = prescription.TargetRir is not null &&
                       sets.Any(x => x.Rir is null || x.Rir.Value < prescription.TargetRir.Value);
        var atTop = sets.All(x => x.Repetitions!.Value >= prescription.MaxRepetitions);
        return new(true, false, string.Empty, load, sets, belowMinimum, belowRir, atTop);
    }

    private static string? ValidatePrescription(TrainingExerciseProgressionPrescription p)
    {
        if (string.IsNullOrWhiteSpace(p.ExerciseId) || p.PlannedWorkingSets is < 1 or > 20 ||
            p.MinRepetitions is < 1 or > 100 || p.MaxRepetitions < p.MinRepetitions ||
            p.MaxRepetitions > 100 || p.TargetRir is < 0 or > 10 ||
            !Enum.IsDefined(p.ProgressionRule) ||
            p.SmallestLoadIncrementKg is <= 0 ||
            p.MaximumLoadIncreasePercent is <= 0 or > 10)
            return "La prescripción del ejercicio contiene valores incompletos o fuera de rango.";
        return null;
    }

    private static TrainingExerciseProgressionRecommendation Recommendation(
        TrainingExerciseProgressionAction action, string explanation) =>
        new(action, explanation, null, null, null, null);

    private sealed record ExposureAnalysis(
        bool IsUsable,
        bool Invalid,
        string Explanation,
        decimal LoadKg,
        IReadOnlyList<TrainingExecutedSet> Sets,
        bool BelowMinimumRepetitions,
        bool BelowTargetRir,
        bool AtTopOfRange)
    {
        public static ExposureAnalysis Failure(string explanation, bool invalid = false) =>
            new(false, invalid, explanation, 0m, Array.Empty<TrainingExecutedSet>(), false, false, false);
    }
}

public sealed record TrainingExerciseProgressionPrescription(
    string ExerciseId,
    int PlannedWorkingSets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRir,
    TrainingProgressionRule ProgressionRule,
    decimal? SmallestLoadIncrementKg = null,
    decimal? MaximumLoadIncreasePercent = null);

public sealed record TrainingExerciseExposure(
    IReadOnlyList<TrainingExecutedSet> WorkingSets,
    bool SessionCompleted = true,
    bool RecoveryInsufficient = false,
    bool PainReported = false,
    bool ProgressionEligible = true,
    TrainingExerciseProgressionPrescriptionSnapshot? HistoricalPrescription = null,
    string? SkippedSetReason = null);

public sealed record TrainingExerciseProgressionPrescriptionSnapshot(
    int? PlannedWorkingSets,
    int? MinRepetitions,
    int? MaxRepetitions,
    decimal? TargetRir);

public sealed record TrainingExecutedSet(
    string Kind,
    decimal? WeightKg,
    int? Repetitions,
    decimal? Rir,
    bool Completed);

public enum TrainingExerciseProgressionAction
{
    InvalidPrescription,
    InvalidData,
    InsufficientData,
    NotApplicable,
    HoldAndReview,
    AddRepetitions,
    ConfirmTopOfRange,
    IncreaseLoad,
    StagnationReview,
    ReduceLoad
}

public sealed record TrainingExerciseProgressionRecommendation(
    TrainingExerciseProgressionAction Action,
    string Explanation,
    decimal? SuggestedLoadKg,
    decimal? LoadIncreaseKg,
    IReadOnlyList<int>? SuggestedRepetitionsBySet,
    decimal? MaximumLoadIncreasePercent);

/// <summary>
/// Turns an active progression hold (set by the automatic adaptation on future sessions) into an effective
/// constraint: no recommendation that increases load or repetitions survives while it is active. Reductions and
/// non-progressive outcomes are kept, because a hold must never block a safety-driven decrease.
/// </summary>
public static class TrainingProgressionHold
{
    public static TrainingExerciseProgressionRecommendation Apply(TrainingExerciseProgressionRecommendation result, bool holdActive)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!holdActive) return result;
        return result.Action is TrainingExerciseProgressionAction.IncreaseLoad
            or TrainingExerciseProgressionAction.AddRepetitions
            or TrainingExerciseProgressionAction.ConfirmTopOfRange
            ? new(TrainingExerciseProgressionAction.HoldAndReview,
                "Progresión en pausa: se mantiene la carga y las repeticiones actuales hasta una nueva evaluación.",
                null, null, null, null)
            : result;
    }
}
