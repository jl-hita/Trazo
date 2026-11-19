using System.Globalization;
using System.Text.Json;
using Trazo.Training;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>An earlier plan of the user that can still teach something: only generated hypertrophy plans, with what they were generated from.</summary>
public sealed record PlanHistory(
    Guid PlanId, int SessionsPerPeriod, string Split, IReadOnlyList<TrainingMuscleBaseline> Baselines, IReadOnlyList<AppliedTarget> Applied);

/// <summary>The new plan's own structure and the clock, plus the earlier plans of the same user. Nothing here comes from another user.</summary>
public sealed record CrossPlanContext(int SessionsPerPeriod, string Split, DateTimeOffset Now, IReadOnlyList<PlanHistory> EarlierPlans);

/// <summary>What the history of earlier plans says about where a muscle should start in a new plan, expressed as sets over the new baseline.</summary>
public sealed record CarryOver(
    decimal Delta, decimal RawDelta, int AgeDays, decimal AgeFactor, decimal EffectiveConfidence, Guid FromPlanId,
    bool SameStructure, bool SamePriority, string Note)
{
    public string SignalsJson() => JsonSerializer.Serialize(new
    {
        carriedDeltaSets = Delta, learnedDeltaSets = RawDelta, evidenceAgeDays = AgeDays, ageFactor = AgeFactor,
        effectiveConfidence = EffectiveConfidence, fromPlan = FromPlanId, sameStructure = SameStructure, samePriority = SamePriority,
        policy = LongitudinalPolicy.Version
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// Moves what the user's earlier plans learned about a muscle into a new plan WITHOUT copying a volume. What is carried is the offset
/// the earlier plan had earned over ITS baseline (how much more or less than the standard dose of its level and priority this user
/// handled), re-applied over the NEW baseline, so a change of frequency or priority cannot import an absolute number. The offset
/// is the very decision the earlier plan would have taken for its next block (same rules, same thresholds: two favorable blocks and
/// confidence 0.65 to go up), so the new plan never lowers the bar. It is then weakened:
/// - by time: full weight for <see cref="LongitudinalPolicy.CarryOverFullWeightDays"/> after the last evidence of the muscle, then a
///   linear fade to nothing at <see cref="LongitudinalPolicy.CarryOverExpiryDays"/> (a long break counts as old history);
/// - by context: an increase is dropped when the weekly structure (days, split) or the priority of the muscle changed, because it
///   was earned in other conditions; a reduction is kept, since poor tolerance is the safer thing to remember;
/// - by size: an increase is capped at <see cref="LongitudinalPolicy.MaximumCarriedIncreaseSets"/> and needs the faded confidence to
///   still reach <see cref="LongitudinalPolicy.IncreaseMinimumConfidence"/>; a reduction needs the medium level.
/// It only sets the starting reference: the blocks of the new plan are then decided by the ordinary rules with the data of the new plan.
/// </summary>
public static class CrossPlanCarry
{
    /// <param name="earlierBlocks">Blocks of the muscle from the user's earlier eligible plans, oldest first.</param>
    public static CarryOver? Compute(
        TrainingMuscleBaseline baseline, CrossPlanContext context, IReadOnlyList<BlockEvidence> earlierBlocks)
    {
        if (earlierBlocks.Count == 0) return null;
        var muscle = baseline.Muscle.ToString();
        var from = context.EarlierPlans.FirstOrDefault(p => p.PlanId == earlierBlocks[^1].PlanId);
        var oldBaseline = from?.Baselines.FirstOrDefault(b => b.Muscle == baseline.Muscle);
        if (from is null || oldBaseline is null) return null;

        var evidenceAt = earlierBlocks.Where(b => !b.IsDeload).SelectMany(b => b.Periods)
            .Where(p => (p.Quality == "valid" || p.Quality == "partial") && p.ProcessedThrough > DateTimeOffset.MinValue)
            .Select(p => (DateTimeOffset?)p.ProcessedThrough).Max();
        if (evidenceAt is null) return null;

        var profile = MuscleResponseEstimator.Estimate(muscle, earlierBlocks);
        var lastApplied = from.Applied.Where(a => a.Muscle == muscle).OrderByDescending(a => a.MesocycleSequence)
            .Select(a => (decimal?)a.Sets).FirstOrDefault();
        var old = LongitudinalVolumePlanner.Decide(oldBaseline, lastApplied, profile);
        var rawDelta = old.ProposedSets - oldBaseline.Target;

        var ageDays = Math.Max(0, (int)(context.Now - evidenceAt.Value).TotalDays);
        var factor = ageDays <= LongitudinalPolicy.CarryOverFullWeightDays ? 1m
            : ageDays >= LongitudinalPolicy.CarryOverExpiryDays ? 0m
            : 1m - (ageDays - LongitudinalPolicy.CarryOverFullWeightDays) / (decimal)(LongitudinalPolicy.CarryOverExpiryDays - LongitudinalPolicy.CarryOverFullWeightDays);
        var confidence = Math.Round(profile.ConfidenceScore * factor, 2);
        var sameStructure = from.SessionsPerPeriod == context.SessionsPerPeriod && string.Equals(from.Split, context.Split, StringComparison.Ordinal);
        var samePriority = oldBaseline.Priority == baseline.Priority;

        var delta = Math.Round(rawDelta * factor, MidpointRounding.AwayFromZero);
        string note;
        if (rawDelta == 0m) note = "el plan anterior no justificaba partir de un volumen distinto del habitual";
        else if (rawDelta > 0m)
        {
            if (!sameStructure || !samePriority) { delta = 0m; note = "el aumento se ganó con otra estructura semanal o prioridad y no se traslada"; }
            else if (confidence < LongitudinalPolicy.IncreaseMinimumConfidence) { delta = 0m; note = "el tiempo transcurrido resta fiabilidad al historial y no se traslada el aumento"; }
            else
            {
                delta = Math.Min(delta, LongitudinalPolicy.MaximumCarriedIncreaseSets);
                note = delta == rawDelta ? "se traslada el aumento ganado en el plan anterior" : "se traslada parte del aumento ganado en el plan anterior";
            }
        }
        else
        {
            if (confidence < LongitudinalPolicy.MediumConfidence) { delta = 0m; note = "la reducción del plan anterior es demasiado antigua para trasladarse"; }
            else note = delta == rawDelta ? "se traslada la reducción del plan anterior por prudencia" : "se traslada parte de la reducción del plan anterior";
        }
        return new CarryOver(delta, rawDelta, ageDays, Math.Round(factor, 2), confidence, from.PlanId, sameStructure, samePriority, note);
    }

    /// <summary>The audited decision for the first block of a new plan, built from the carry (never from the new plan's own data: it has none yet).</summary>
    public static VolumeDecision Decision(TrainingMuscleBaseline baseline, CarryOver carry, BlockSummary? lastEarlierBlock)
    {
        var name = MuscleNames.Es(baseline.Muscle.ToString());
        var proposed = Math.Clamp(baseline.Target + carry.Delta,
            Math.Max(baseline.Floor, baseline.Target - LongitudinalPolicy.MaximumBelowBaselineSets),
            Math.Min(baseline.Ceiling, baseline.Target + LongitudinalPolicy.MaximumAboveBaselineSets));
        var level = carry.EffectiveConfidence >= LongitudinalPolicy.HighConfidence ? ConfidenceLevel.High
            : carry.EffectiveConfidence >= LongitudinalPolicy.MediumConfidence ? ConfidenceLevel.Medium : ConfidenceLevel.Low;
        var action = proposed != baseline.Target ? VolumeAction.Carry : carry.RawDelta == 0m ? VolumeAction.Hold : VolumeAction.Abstain;
        var inv = CultureInfo.InvariantCulture;
        var reason = proposed != baseline.Target
            ? $"Se parte de lo aprendido en tu plan anterior para {name} ({baseline.Target:0} → {proposed:0} series semanales): {carry.Note}. Hace {carry.AgeDays} días de la última evidencia (peso {carry.AgeFactor.ToString("0.00", inv)}). Se confirmará o corregirá con los datos de este plan."
            : $"Se parte del volumen habitual de {name} ({baseline.Target:0} series semanales): {carry.Note}.";
        return new VolumeDecision(
            baseline.Muscle.ToString(), action, baseline.Target, baseline.Target, proposed, level, carry.EffectiveConfidence,
            lastEarlierBlock?.FirstPeriod ?? 0, lastEarlierBlock?.LastPeriod ?? 0, reason, carry.SignalsJson());
    }
}
