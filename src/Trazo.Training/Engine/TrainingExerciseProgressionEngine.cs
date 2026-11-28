namespace Trazo.Training;

/// <summary>
/// Exercise-level double-progression engine. It produces a suggestion for the next exposure;
/// it never edits a prescription, changes completed history, or applies a load automatically.
/// </summary>
public static class TrainingExerciseProgressionEngine
{
    private const int RequiredConsecutiveTopRangeExposures = 2;
    private const decimal DefaultMaximumLoadIncreasePercent = 5m;
    // Without the reserve written down, effort is unknown: one more session at the top is asked for instead.
    private const int RequiredTopRangeExposuresWithoutReserve = 3;
    // Calibration: a load far below what the person can do moves faster than one step per two sessions. The surplus asked of
    // every set, and the largest single jump, are internal product values.
    private const decimal CalibrationReserveSurplus = 2m;
    private const decimal CalibrationMaximumIncreasePercent = 10m;
    /// <summary>Reasons for leaving a set out that say nothing about the load.</summary>
    private static readonly string[] OutsideSkipReasons = ["time", "equipment", "motivation"];

    /// <summary>
    /// The load an exposure was really done with: that of most completed sets, the heaviest on a tie. A back-off set or a single
    /// heavier attempt does not change it. Null without a completed set with weight.
    /// </summary>
    public static decimal? MainLoad(IEnumerable<TrainingExecutedSet> sets) =>
        sets.Where(set => set.Completed && set.WeightKg is > 0)
            .GroupBy(set => set.WeightKg!.Value)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key)
            .Select(group => (decimal?)group.Key)
            .FirstOrDefault();

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

        // One failure right after a calibration jump is enough to take it back.
        var back = TryUndoCalibration(prescription, exposures);
        if (back is not null) return back;

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
            var nextRepetitions = PerPrescribedSet(prescription, latest.Sets
                .Select(set => Math.Min(prescription.MaxRepetitions, set.Repetitions!.Value + 1))
                .ToArray());
            return new(
                TrainingExerciseProgressionAction.AddRepetitions,
                "Mantén la carga y añade como máximo una repetición por serie en la próxima exposición, sin superar el rango prescrito ni perder el RIR objetivo.",
                latest.LoadKg,
                null,
                nextRepetitions,
                null);
        }

        if (TryCalibrate(prescription, latest) is { } calibrated) return calibrated;

        // A single good session is not enough to justify a load jump. Requiring two
        // consecutive top-of-range exposures reduces reactions to day-to-day noise.
        // A block changes sets and reserve from week to week by design, so an earlier exposure is judged against what IT
        // prescribed. Only a different repetition range makes it incomparable.
        var streak = new List<ExposureAnalysis> { latest };
        for (var index = exposures.Length - 2; index >= 0; index--)
        {
            if (AsPrescribedThen(prescription, exposures[index]) is not { } then) break;
            var previous = AnalyzeExposure(then, exposures[index]);
            if (!exposures[index].ProgressionEligible || !exposures[index].SessionCompleted ||
                !previous.IsUsable || previous.BelowTargetRir || previous.BelowMinimumRepetitions ||
                !previous.AtTopOfRange || previous.LoadKg != latest.LoadKg ||
                exposures[index].RecoveryInsufficient || exposures[index].PainReported)
                break;
            streak.Add(previous);
        }

        var reserveMissing = streak.Take(RequiredConsecutiveTopRangeExposures).Any(x => x.ReserveMissing);
        var requiredExposures = reserveMissing ? RequiredTopRangeExposuresWithoutReserve : RequiredConsecutiveTopRangeExposures;
        if (streak.Count < requiredExposures)
            return Recommendation(TrainingExerciseProgressionAction.ConfirmTopOfRange, reserveMissing
                ? "Has alcanzado el máximo del rango. Sin el RIR apuntado hacen falta tres sesiones seguidas; apúntalo para subir antes."
                : "Has alcanzado el máximo del rango. Repite la carga y confirma el resultado en otra sesión antes de subir peso.");
        var confirmed = reserveMissing
            ? "Tres exposiciones consecutivas cumplen el rango."
            : "Dos exposiciones consecutivas cumplen el rango y el RIR.";

        var maximumIncreasePercent = prescription.MaximumLoadIncreasePercent ?? DefaultMaximumLoadIncreasePercent;
        var maximumIncrease = decimal.Round(
            latest.LoadKg * maximumIncreasePercent / 100m, 2, MidpointRounding.AwayFromZero);
        if (prescription.SmallestLoadIncrementKg is not > 0)
            return new(
                TrainingExerciseProgressionAction.IncreaseLoad,
                $"{confirmed} Sube solo el menor incremento disponible del material, sin superar el {maximumIncreasePercent:0.##} % de la carga actual; registra el nuevo peso para calibrar futuras recomendaciones.",
                null, null, Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
                maximumIncreasePercent);

        var increment = prescription.SmallestLoadIncrementKg.Value;
        if (increment > maximumIncrease)
        {
            // A ceiling written for this exercise is a limit set on purpose: it is never exceeded.
            if (prescription.MaximumLoadIncreasePercent is not null)
                return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                    $"El incremento disponible ({increment:0.##} kg) supera el límite conservador configurado del {maximumIncreasePercent:0.##} % ({maximumIncrease:0.##} kg). Mantén la carga o ajusta el material; no fuerces un salto excesivo.");

            // With the default ceiling, a light load can never move by the smallest step the equipment has. The step is then
            // earned with repetitions: enough of them that the new load is expected to stay inside the range.
            var required = RepetitionsThatSupport(prescription, latest.LoadKg, latest.LoadKg + increment);
            if (!streak.Take(requiredExposures).All(x => x.Sets.All(set => set.Repetitions!.Value >= required)))
                return new(
                    TrainingExerciseProgressionAction.AddRepetitions,
                    $"Has alcanzado el máximo del rango. El siguiente peso disponible supone subir {increment:0.##} kg de golpe. Mantén la carga y llega a {required} repeticiones por serie con el mismo RIR antes de subir.",
                    latest.LoadKg,
                    null,
                    PerPrescribedSet(prescription, latest.Sets
                        .Select(set => Math.Max(set.Repetitions!.Value, Math.Min(required, set.Repetitions!.Value + 1))).ToArray()),
                    null);
        }

        var suggestedLoad = decimal.Round(latest.LoadKg + increment, 2, MidpointRounding.AwayFromZero);
        return new(
            TrainingExerciseProgressionAction.IncreaseLoad,
            $"{confirmed} Aumenta un único incremento mínimo del material y vuelve al extremo inferior del rango de repeticiones.",
            suggestedLoad,
            increment,
            Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
            maximumIncreasePercent);
    }

    /// <summary>
    /// Every set at the top of the range with at least two repetitions more in reserve than asked, all of them written down:
    /// the load is far below the person's. One session is enough, and the new load is the one estimated for the bottom of
    /// the range, never more than ten percent up nor past a ceiling written for the exercise. Null when that is not the case,
    /// or when the equipment's step does not fit under the ceiling.
    /// </summary>
    private static TrainingExerciseProgressionRecommendation? TryCalibrate(
        TrainingExerciseProgressionPrescription prescription, ExposureAnalysis latest)
    {
        if (prescription.TargetRir is not { } target || prescription.SmallestLoadIncrementKg is not > 0 ||
            !latest.AtTopOfRange || !HasSurplus(latest.Sets, target))
            return null;
        var increment = prescription.SmallestLoadIncrementKg.Value;
        var percent = prescription.MaximumLoadIncreasePercent ?? CalibrationMaximumIncreasePercent;
        var oneRepMax = latest.Sets.Average(set => TrainingLoadEstimator.EstimateOneRepMax(set.WeightKg!.Value, set.Repetitions!.Value, set.Rir));
        var estimated = TrainingLoadEstimator.LoadForRepetitions(oneRepMax, prescription.MinRepetitions, target);
        var bounded = Math.Min(estimated, latest.LoadKg * (1 + percent / 100m));
        var load = latest.LoadKg + decimal.Floor((bounded - latest.LoadKg) / increment) * increment;
        if (load <= latest.LoadKg) return null;
        return new(
            TrainingExerciseProgressionAction.IncreaseLoad,
            "Todas las series llegaron al máximo del rango con al menos dos repeticiones más en reserva de lo previsto: la carga está muy por debajo de la tuya. Se sube más de lo habitual, como máximo un 10 %.",
            load, load - latest.LoadKg,
            Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
            percent);
    }

    private static bool HasSurplus(IReadOnlyList<TrainingExecutedSet> sets, decimal target) =>
        sets.Count > 0 && sets.All(set => set.Rir is { } rir && rir >= target + CalibrationReserveSurplus);

    /// <summary>
    /// The latest exposure fell under the range with a load that came from a calibration jump (the one before was at the top
    /// with the surplus, and lighter by more than the usual step): back to the load before.
    /// </summary>
    private static TrainingExerciseProgressionRecommendation? TryUndoCalibration(
        TrainingExerciseProgressionPrescription prescription, IReadOnlyList<TrainingExerciseExposure> exposures)
    {
        if (exposures.Count < 2) return null;
        var latest = exposures[^1];
        var before = exposures[^2];
        if (!latest.ProgressionEligible || latest.RecoveryInsufficient || latest.WorkingSets is null || before.WorkingSets is null)
            return null;
        if (AsPrescribedThen(prescription, before) is not { TargetRir: { } target } then) return null;
        if (MainLoad(latest.WorkingSets) is not { } now || MainLoad(before.WorkingSets) is not { } was) return null;
        var usual = Math.Max(
            prescription.SmallestLoadIncrementKg ?? TrainingLoadEstimator.IncrementFor(was), was * DefaultMaximumLoadIncreasePercent / 100m);
        if (now - was <= usual) return null;
        var light = before.WorkingSets.Where(set => set.Completed && set.WeightKg == was && set.Repetitions is not null).ToArray();
        if (!HasSurplus(light, target) || light.Any(set => set.Repetitions < then.MaxRepetitions)) return null;
        var failed = latest.WorkingSets.Any(set => set.Completed && set.WeightKg == now && set.Repetitions < prescription.MinRepetitions)
            || latest.WorkingSets.Any(set => !set.Completed && string.Equals(set.SkippedReason, "load_too_heavy", StringComparison.OrdinalIgnoreCase));
        if (!failed) return null;
        return new(
            TrainingExerciseProgressionAction.ReduceLoad,
            "Con la última subida no se llegó al mínimo del rango: vuelve a la carga anterior y progresa desde ahí.",
            was, now - was, Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(), null);
    }

    /// <summary>
    /// Repetitions at <paramref name="load"/> (with the target reserve) whose Epley estimate covers the minimum of the range
    /// at <paramref name="nextLoad"/>. Never less than the top of the range.
    /// </summary>
    private static int RepetitionsThatSupport(
        TrainingExerciseProgressionPrescription prescription, decimal load, decimal nextLoad)
    {
        var rir = prescription.TargetRir ?? 0m;
        var needed = (int)Math.Ceiling(nextLoad * (30m + prescription.MinRepetitions + rir) / load - 30m - rir);
        return Math.Clamp(needed, prescription.MaxRepetitions, 100);
    }

    /// <summary>The prescription an earlier exposure was done against; null when its repetition range was another one.</summary>
    private static TrainingExerciseProgressionPrescription? AsPrescribedThen(
        TrainingExerciseProgressionPrescription current, TrainingExerciseExposure exposure)
    {
        if (exposure.HistoricalPrescription is not { } then) return current;
        if (then.MinRepetitions != current.MinRepetitions || then.MaxRepetitions != current.MaxRepetitions ||
            then.PlannedWorkingSets is not { } sets)
            return null;
        return current with { PlannedWorkingSets = sets, TargetRir = then.TargetRir };
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

            // Each exposure fails against its own prescription: a week with fewer sets is not an incomplete one.
            if (AsPrescribedThen(prescription, exposure) is not { } then) return null;
            var completed = exposure.WorkingSets.Where(set => set.Completed).ToArray();
            var belowMinimum = completed.Any(set => set.Repetitions is < 1 || set.Repetitions < then.MinRepetitions);
            var incomplete = completed.Length < then.PlannedWorkingSets;
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

        if (recent.Any(x => AsPrescribedThen(prescription, x) is null))
            return false;
        var analyses = recent.Select(x => AnalyzeExposure(AsPrescribedThen(prescription, x)!, x)).ToArray();
        if (analyses.Any(x => !x.IsUsable || x.BelowMinimumRepetitions || x.BelowTargetRir))
            return false;
        if (analyses.Select(x => x.LoadKg).Distinct().Count() != 1)
            return false;

        // Totals of different numbers of sets say nothing about progress.
        if (analyses.Select(x => x.Sets.Count).Distinct().Count() != 1)
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

        // One prescribed set may differ (another load, or left out for an outside reason); the rest are what is judged.
        var needed = Math.Max(1, prescription.PlannedWorkingSets - 1);
        const string wrongCount = "El número de series efectivas registradas no coincide con las prescritas. Si no pudiste completarlas, indica el motivo (por ejemplo, «peso demasiado alto») para que el sistema pueda ajustar la carga.";
        const string missingData = "Faltan datos válidos de carga, repeticiones o finalización en alguna serie efectiva.";
        if (exposure.WorkingSets is null || exposure.WorkingSets.Count < prescription.PlannedWorkingSets)
            return ExposureAnalysis.Failure(wrongCount);

        var all = exposure.WorkingSets;
        if (all.Any(x => !string.Equals(x.Kind, "working", StringComparison.Ordinal) || x.Rir is < 0 or > 10 ||
                         (x.Completed
                             ? x.WeightKg is null or <= 0 || x.Repetitions is null or < 1 or > 100
                             : !OutsideSkipReasons.Contains(x.SkippedReason, StringComparer.OrdinalIgnoreCase))))
            return ExposureAnalysis.Failure(missingData);

        if (MainLoad(all) is not { } load)
            return ExposureAnalysis.Failure(missingData);
        var sets = all.Where(x => x.Completed && x.WeightKg == load).ToArray();
        if (sets.Length < needed)
            return all.Count(x => x.Completed) < needed
                ? ExposureAnalysis.Failure(wrongCount)
                : ExposureAnalysis.Failure("Las series usan cargas distintas. No se calcula una progresión única hasta que la prescripción modele esas cargas.", invalid: true);

        // A set at another load counts against the exposure only when it fell short of the range.
        var belowMinimum = all.Any(x => x.Completed && x.Repetitions!.Value < prescription.MinRepetitions);
        // A reserve that was written down and fell short holds; one that was not written down is unknown, not short.
        var belowRir = prescription.TargetRir is not null &&
                       sets.Any(x => x.Rir is not null && x.Rir.Value < prescription.TargetRir.Value);
        var reserveMissing = prescription.TargetRir is not null && sets.Any(x => x.Rir is null);
        var atTop = sets.All(x => x.Repetitions!.Value >= prescription.MaxRepetitions);
        return new(true, false, string.Empty, load, sets, belowMinimum, belowRir, atTop, reserveMissing);
    }

    private static int[] PerPrescribedSet(TrainingExerciseProgressionPrescription prescription, int[] judged) =>
        judged.Length >= prescription.PlannedWorkingSets
            ? judged
            : [.. judged, .. Enumerable.Repeat(judged.Min(), prescription.PlannedWorkingSets - judged.Length)];

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
        bool AtTopOfRange,
        bool ReserveMissing = false)
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
    bool Completed,
    string? SkippedReason = null);

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
