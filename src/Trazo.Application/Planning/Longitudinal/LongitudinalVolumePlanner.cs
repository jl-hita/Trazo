using System.Text.Json;
using Trazo.Training;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>Spanish names of the muscle groups for the explanations the user reads.</summary>
public static class MuscleNames
{
    public static string Es(string muscle) => muscle switch
    {
        "Chest" => "pecho",
        "Lats" => "dorsales",
        "UpperBack" => "espalda alta",
        "Deltoids" => "hombros",
        "Biceps" => "bíceps",
        "Triceps" => "tríceps",
        "Quadriceps" => "cuádriceps",
        "Hamstrings" => "isquiosurales",
        "Glutes" => "glúteos",
        "Calves" => "gemelos",
        _ => muscle.ToLowerInvariant()
    };
}

/// <summary>
/// Decides, for one muscle and one new block, whether its weekly target stays, goes down one set or comes back one set towards the
/// plan's own target. Each outcome has one cause and says it:
/// - <b>Decrease</b>: the last block with enough weeks showed a repeated pattern of falling performance with fatigue.
/// - <b>Restore</b>: an earlier reduction is given back one step because the last block no longer shows that pattern; never above the
///   plan's own target, and never while pain is recorded in the recent blocks.
/// - <b>Hold</b>: the evidence was read and the target stays (pain, floor reached, nothing to restore).
/// - <b>Abstain</b>: there is not enough evidence to read; the target stays EXACTLY as the previous block had it. Abstaining never lowers
///   a target and never raises one.
/// The target can never go above the baseline: nothing here infers from a better performance that a muscle wants more volume.
/// </summary>
public static class LongitudinalVolumePlanner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="previousApplied">The weekly target the previous block of this plan used for the muscle, or null when none.</param>
    /// <param name="blocks">Summaries of the finished non-deload blocks of this plan for the muscle, oldest first.</param>
    public static VolumeDecision Decide(TrainingMuscleBaseline baseline, decimal? previousApplied, IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(blocks);
        var muscle = baseline.Muscle.ToString();
        var name = MuscleNames.Es(muscle);
        var floor = Math.Min(baseline.Target, Math.Max(baseline.Floor, baseline.Target - LongitudinalPolicy.MaximumBelowBaselineSets));
        // The previous dose, held inside the band this baseline allows (the baseline may have moved since: priorities, days).
        var previous = Math.Clamp(previousApplied ?? baseline.Target, floor, baseline.Target);
        var last = blocks.LastOrDefault(b => b.Verdict != BlockVerdict.Insufficient);
        var pain = blocks.TakeLast(2).Any(b => b.Pain);

        VolumeDecision Make(VolumeAction action, decimal proposed, string reason) => new(
            muscle, action, baseline.Target, previous, proposed, last?.FirstPeriod ?? 0, last?.LastPeriod ?? 0, reason, Signals(baseline, floor, previous, last, pain));

        if (last is null)
            return Make(VolumeAction.Abstain, previous,
                $"No hay semanas válidas suficientes para leer cómo va {name}: el volumen se queda como estaba ({previous:0} series semanales).");

        if (last.Verdict == BlockVerdict.Unfavorable)
        {
            var lowered = Math.Max(previous - LongitudinalPolicy.StepSets, floor);
            return lowered < previous
                ? Make(VolumeAction.Decrease, lowered,
                    $"Se baja una serie semanal de {name} (de {previous:0} a {lowered:0}) porque en {last.UnfavorablePeriods} semanas del bloque anterior el rendimiento bajó junto con señales de fatiga o series por debajo del rango.")
                : Make(VolumeAction.Hold, previous,
                    $"Se mantiene {name} en su mínimo ({previous:0} series semanales) aunque se repiten señales de fatiga: no se baja más de forma automática.");
        }

        if (previous < baseline.Target)
        {
            if (pain)
                return Make(VolumeAction.Hold, previous,
                    $"Se mantiene {name} en {previous:0} series semanales: se registró dolor en los últimos bloques y el dolor impide devolver volumen automáticamente.");
            var restored = Math.Min(previous + LongitudinalPolicy.StepSets, baseline.Target);
            return Make(VolumeAction.Restore, restored,
                $"Se devuelve una serie semanal de {name} (de {previous:0} a {restored:0}, sin pasar de las {baseline.Target:0} de tu plan) porque el último bloque ya no muestra fatiga con bajada de rendimiento.");
        }

        return Make(VolumeAction.Hold, previous,
            $"Se mantiene el volumen de {name} ({previous:0} series semanales): no hay señales que lo cambien. El volumen no sube por una mejora de rendimiento.");
    }

    private static string Signals(TrainingMuscleBaseline baseline, decimal floor, decimal previous, BlockSummary? last, bool pain) =>
        JsonSerializer.Serialize(new
        {
            priority = baseline.Priority.ToString(),
            floor,
            previous,
            verdict = last?.Verdict.ToString(),
            validPeriods = last?.ValidPeriods,
            partialPeriods = last?.PartialPeriods,
            decliningPeriods = last?.DecliningPeriods,
            unfavorablePeriods = last?.UnfavorablePeriods,
            fatiguedPeriods = last?.FatiguedPeriods,
            performedSets = last?.PerformedSets,
            prescribedSets = last?.PrescribedSets,
            painInRecentBlocks = pain,
            policy = LongitudinalPolicy.Version
        }, Json);
}
