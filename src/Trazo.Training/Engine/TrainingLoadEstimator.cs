namespace Trazo.Training;

/// <summary>
/// Pure helper that turns the lifter's last recorded work sets into a suggested working load for
/// a (possibly new) prescription. It never invents a load without a reference: with no usable
/// history the result is <c>null</c> and the prescription simply carries no weight.
///
/// Two paths, both internal product rules reviewable by the professional:
///  1. The last performance already sat inside the prescribed rep range with enough effort reserve
///     -> keep the same load (double progression raises repetitions first, load later).
///  2. Otherwise (reps below the range, above it, or the range changed, e.g. a new strength block)
///     -> estimate a one-rep max from the performance and back-calculate the load for the start of
///     the new range at the target RIR. The estimate uses the common Epley-type relationship
///     e1RM = load x (1 + (reps + RIR) / 30). It is an approximation, less reliable at high
///     repetitions, so the change is capped (MaximumChangePercent) and rounded to the smallest
///     available load increment. The 30 divisor is the usual convention; the cap is a product limit.
/// </summary>
public static class TrainingLoadEstimator
{
    public const decimal DefaultMaximumChangePercent = 10m;
    public const decimal DefaultLoadIncrementKg = 2.5m;

    public static TrainingLoadSuggestion? Suggest(
        IReadOnlyList<TrainingExecutedSet> lastWorkingSets,
        int minRepetitions,
        int maxRepetitions,
        decimal? targetRir,
        decimal? smallestIncrementKg = null,
        decimal maximumChangePercent = DefaultMaximumChangePercent)
    {
        ArgumentNullException.ThrowIfNull(lastWorkingSets);
        if (minRepetitions < 1 || maxRepetitions < minRepetitions) return null;
        var sets = lastWorkingSets
            .Where(set => set.Completed && set.WeightKg is > 0 && set.Repetitions is >= 1 and <= 100)
            .ToArray();
        if (sets.Length == 0) return null;
        var increment = smallestIncrementKg is > 0 ? smallestIncrementKg.Value : DefaultLoadIncrementKg;

        // Use the heaviest load of the exposure as the reference; mixed loads are not averaged.
        var load = sets.Max(set => set.WeightKg!.Value);
        var atLoad = sets.Where(set => set.WeightKg == load).ToArray();
        var minReps = atLoad.Min(set => set.Repetitions!.Value);
        var avgReps = (decimal)atLoad.Average(set => set.Repetitions!.Value);
        var rirValues = atLoad.Where(set => set.Rir is not null).Select(set => set.Rir!.Value).ToArray();
        var avgRir = rirValues.Length == atLoad.Length ? rirValues.Average() : (decimal?)null;
        var effortOk = targetRir is null || avgRir is null || avgRir.Value >= targetRir.Value - 0.5m;

        if (minReps >= minRepetitions && minReps <= maxRepetitions && effortOk)
            return new(Round(load, increment), "Misma carga que la última vez: el rendimiento ya estaba dentro del rango y con el esfuerzo previsto.");

        var e1rm = atLoad.Average(set => EstimateOneRepMax(set.WeightKg!.Value, set.Repetitions!.Value, set.Rir));
        var targetReps = minRepetitions;
        var estimated = LoadForRepetitions(e1rm, targetReps, targetRir ?? 2m);
        var lower = load * (1 - maximumChangePercent / 100m);
        var upper = load * (1 + maximumChangePercent / 100m);
        var bounded = Math.Clamp(estimated, lower, upper);
        var rounded = Round(bounded, increment);
        var direction = rounded < load ? "reducida" : rounded > load ? "aumentada" : "mantenida";
        return new(rounded,
            $"Carga {direction} respecto a la última ({load:0.##} kg): estimación a partir de {avgReps:0.#} repeticiones " +
            $"(fórmula de Epley, aproximada; cambio limitado al {maximumChangePercent:0.#} %). Revísala con tu profesional.");
    }

    public static decimal EstimateOneRepMax(decimal loadKg, int repetitions, decimal? rir) =>
        loadKg * (1 + (repetitions + (rir ?? 0m)) / 30m);

    public static decimal LoadForRepetitions(decimal oneRepMax, int repetitions, decimal rir) =>
        oneRepMax / (1 + (repetitions + rir) / 30m);

    public static decimal Round(decimal loadKg, decimal incrementKg) =>
        Math.Max(incrementKg, decimal.Round(loadKg / incrementKg, 0, MidpointRounding.AwayFromZero) * incrementKg);
}

public sealed record TrainingLoadSuggestion(decimal LoadKg, string Basis);

/// <summary>Last recorded performance of one exercise, used to prescribe a load for a new plan.</summary>
public sealed record TrainingLoadReference(
    string ExerciseId, decimal WeightKg, int Repetitions, decimal? Rir = null, int Sets = 1);
