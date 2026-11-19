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
/// Decides, for one muscle and one new block, whether the weekly target stays, goes up one set or goes down one set. It never
/// invents a volume: it moves the target the priority planner already chose by at most <see cref="LongitudinalPolicy.StepSets"/>
/// per block, inside the limits of <see cref="TrainingMuscleBaseline"/>, and only with enough evidence. When it cannot tell, it
/// abstains and the block uses the plan's own target (never more than the previous block used). It reads, it does not touch the database.
/// </summary>
public static class LongitudinalVolumePlanner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="previousApplied">The weekly target the previous block of this plan used for the muscle, or null when none.</param>
    public static VolumeDecision Decide(TrainingMuscleBaseline baseline, decimal? previousApplied, MuscleResponseProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var muscle = baseline.Muscle.ToString();
        var name = MuscleNames.Es(muscle);
        var previous = Math.Clamp(previousApplied ?? baseline.Target, baseline.Floor, baseline.Ceiling);
        // Without evidence the block never uses more than the previous one did, and never more than the plan's own target.
        var cautious = Math.Min(previous, baseline.Target);
        var last = profile?.LastQualifiedBlock;
        var signals = Signals(baseline, previous, profile, last);

        VolumeDecision Make(VolumeAction action, decimal proposed, string reason) => new(
            muscle, action, baseline.Target, previous, proposed, profile?.Confidence ?? ConfidenceLevel.Low, profile?.ConfidenceScore ?? 0m,
            last?.FirstPeriod ?? 0, last?.LastPeriod ?? 0, reason, signals);

        if (profile is null || profile.ValidBlocks == 0 || last is null)
            return Make(VolumeAction.Abstain, cautious,
                $"No se modifica el volumen de {name} porque hay pocas exposiciones válidas para conocer tu respuesta.");
        if (profile.PainInRecentBlocks)
            return Make(VolumeAction.Abstain, cautious,
                $"No se modifica el volumen de {name} porque se registró dolor; el dolor tiene siempre prioridad sobre el ajuste de volumen y no se aprende de esas sesiones.");
        if (profile.Confidence == ConfidenceLevel.Low)
            return Make(VolumeAction.Abstain, cautious,
                $"La confianza de la estimación para {name} es baja (pocas semanas válidas o entrenamientos irregulares); no se modifica el volumen.");

        if (last.Verdict == BlockVerdict.Unfavorable)
        {
            var floor = Math.Max(baseline.Floor, baseline.Target - LongitudinalPolicy.MaximumBelowBaselineSets);
            var lowered = Math.Max(previous - LongitudinalPolicy.StepSets, floor);
            return lowered < previous
                ? Make(VolumeAction.Decrease, lowered,
                    $"Se reduce ligeramente el volumen de {name} (de {previous:0} a {lowered:0} series semanales) porque se repiten señales de fatiga y deterioro del rendimiento.")
                : Make(VolumeAction.Hold, previous,
                    $"Se mantiene el volumen de {name} en su mínimo ({previous:0} series semanales) aunque se repiten señales de fatiga: no se baja más de forma automática.");
        }

        if (last.Verdict == BlockVerdict.Favorable)
        {
            var next = previous + LongitudinalPolicy.StepSets;
            var cap = Math.Min(baseline.Ceiling, baseline.Target + LongitudinalPolicy.MaximumAboveBaselineSets);
            var blocker =
                baseline.Priority == TrainingMusclePriority.Maintain ? $"{name} está en mantenimiento, así que no se aumenta."
                : last.Adherence < LongitudinalPolicy.MinimumDoseAdherence ? $"no llegaste a completar el volumen prescrito de {name}."
                : last.Regularity < LongitudinalPolicy.MinimumRegularity ? "los entrenamientos han sido irregulares."
                : last.DiscomfortAnswers >= LongitudinalPolicy.DiscomfortAnswersToHold ? $"has indicado molestias en ejercicios de {name}."
                : next > cap ? $"{name} ya está en el máximo que se aumenta automáticamente ({previous:0} series)."
                : profile.ObservedLimitSets is { } limit && next >= limit ? $"con {limit:0} series semanales ya se vio fatiga repetida."
                : null;
            if (blocker is not null)
                return Make(VolumeAction.Hold, previous, $"Se mantiene el volumen de {name} ({previous:0} series semanales): {blocker}");
            return Make(VolumeAction.Increase, next,
                $"Se propone aumentar una serie semanal de {name} (de {previous:0} a {next:0}) porque el rendimiento ha progresado de forma sostenida y la recuperación ha sido favorable.");
        }

        return Make(VolumeAction.Hold, previous,
            $"Se mantiene el volumen de {name} porque los datos actuales no justifican un cambio.");
    }

    private static string Signals(TrainingMuscleBaseline baseline, decimal previous, MuscleResponseProfile? profile, BlockSummary? last) =>
        JsonSerializer.Serialize(new
        {
            priority = baseline.Priority.ToString(),
            floor = baseline.Floor,
            ceiling = baseline.Ceiling,
            previous,
            verdict = last?.Verdict.ToString(),
            validPeriods = last?.ValidPeriods,
            partialPeriods = last?.PartialPeriods,
            favorablePeriods = last?.FavorablePeriods,
            unfavorablePeriods = last?.UnfavorablePeriods,
            fatiguedPeriods = last?.FatiguedPeriods,
            performedSets = last?.PerformedSets,
            prescribedSets = last?.PrescribedSets,
            regularity = last?.Regularity,
            feedbackAnswers = last?.FeedbackAnswers,
            discomfortAnswers = last?.DiscomfortAnswers,
            trend = profile?.Trend.ToString(),
            referenceSets = profile?.ReferenceSets,
            toleratedSets = profile?.ToleratedSets,
            observedLimitSets = profile?.ObservedLimitSets,
            volumeChangeResponse = profile?.VolumeChangeResponse.ToString(),
            deloadResponse = profile?.DeloadResponse.ToString(),
            painInRecentBlocks = profile?.PainInRecentBlocks,
            validBlocks = profile?.ValidBlocks,
            confidenceScore = profile?.ConfidenceScore,
            policy = LongitudinalPolicy.Version
        }, Json);
}
