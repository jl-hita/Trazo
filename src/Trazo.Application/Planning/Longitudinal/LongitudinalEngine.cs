using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>A weekly target a previous block of the plan really used for a muscle.</summary>
public sealed record AppliedTarget(int MesocycleSequence, string Muscle, decimal Sets);

public sealed record LongitudinalResult(
    IReadOnlyDictionary<TrainingMuscleGroup, decimal> LearnedTargets, IReadOnlyList<VolumeDecision> Decisions)
{
    public static readonly LongitudinalResult None = new(new Dictionary<TrainingMuscleGroup, decimal>(), []);
}

/// <summary>
/// The whole longitudinal decision for one new block, as a pure function of data already loaded: for every muscle with a baseline it
/// estimates the response from the observations of finished blocks and decides the weekly target. The infrastructure only loads the
/// observations and stores the result; the tests run this very function, so what they prove is what production does.
/// </summary>
public static class LongitudinalEngine
{
    /// <param name="planOrder">Chronological position of each of the user's plans (older first), so blocks of earlier plans precede these.</param>
    /// <param name="cross">The earlier plans of the SAME user that may teach this one (generated hypertrophy plans) and the new plan's structure;
    /// null means the plan has no earlier history to learn from, and only its own blocks count.</param>
    public static LongitudinalResult Propose(
        Guid planId,
        int slotSequence,
        IReadOnlyList<TrainingMuscleBaseline> baselines,
        IReadOnlyList<MuscleWeekObservation> observations,
        IReadOnlyDictionary<Guid, int> planOrder,
        IReadOnlyList<AppliedTarget> applied,
        CrossPlanContext? cross = null)
    {
        var decisions = new List<VolumeDecision>();
        var learned = new Dictionary<TrainingMuscleGroup, decimal>();
        var eligibleEarlier = cross?.EarlierPlans.Select(p => p.PlanId).ToHashSet() ?? [];
        foreach (var baseline in baselines)
        {
            var muscle = baseline.Muscle.ToString();
            // Only blocks that are over: earlier blocks of this plan and every block of an earlier eligible plan of the same user.
            var blocks = observations
                .Where(o => o.Muscle == muscle && (o.PlanId == planId ? o.MesocycleSequence < slotSequence : eligibleEarlier.Contains(o.PlanId)))
                .GroupBy(o => (o.PlanId, o.MesocycleSequence))
                .OrderBy(g => planOrder.GetValueOrDefault(g.Key.PlanId)).ThenBy(g => g.Key.MesocycleSequence)
                .Select(g => new BlockEvidence(g.Key.PlanId, g.Key.MesocycleSequence, g.Any(o => o.IsDeload), g.OrderBy(o => o.Period).ToList()))
                .ToList();
            var earlierBlocks = blocks.Where(b => b.PlanId != planId).ToList();
            var ownBlocks = blocks.Count - earlierBlocks.Count;
            var previous = applied.Where(a => a.Muscle == muscle && a.MesocycleSequence < slotSequence)
                .OrderByDescending(a => a.MesocycleSequence).Select(a => (decimal?)a.Sets).FirstOrDefault();

            var carry = cross is null ? null : CrossPlanCarry.Compute(baseline, cross, earlierBlocks);
            if (ownBlocks == 0 && previous is null)
            {
                // The first block of a new plan: nothing of this plan to read yet, so the reference comes from the carried history only.
                // The evidence of the earlier plan was already weighed once to compute the carry; it is not weighed again as a new block.
                if (carry is null) continue;
                var summary = MuscleResponseEstimator.Estimate(muscle, earlierBlocks).LastQualifiedBlock;
                var inherited = CrossPlanCarry.Decision(baseline, carry, summary);
                if (inherited.LearnedTarget is { } start) learned[baseline.Muscle] = start;
                decisions.Add(inherited);
                continue;
            }
            if (ownBlocks == 0)
            {
                // A later block generated before any workout of this plan was processed: keep the reference of the previous block.
                var held = Math.Clamp(previous!.Value, baseline.Floor, baseline.Ceiling);
                var hold = new VolumeDecision(
                    muscle, VolumeAction.Hold, baseline.Target, previous.Value, held, ConfidenceLevel.Low, 0m, 0, 0,
                    $"Se mantiene la referencia de {MuscleNames.Es(muscle)}: todavía no hay datos de este plan que la cambien.", "{}");
                if (hold.LearnedTarget is { } kept) learned[baseline.Muscle] = kept;
                decisions.Add(hold);
                continue;
            }
            // The previous dose of this plan, or else the one carried from the earlier plans, is the point the new evidence moves from.
            var from = previous ?? (carry is { Delta: not 0m } c ? baseline.Target + c.Delta : null);
            // The earlier plans' blocks keep counting as evidence only while they still apply (same structure and priority, not faded):
            // otherwise they could complete the "two favorable blocks in a row" of the new plan with data from other conditions.
            var earlierStillApplies = carry is { SameStructure: true, SamePriority: true } && carry.EffectiveConfidence >= LongitudinalPolicy.MediumConfidence;
            var response = MuscleResponseEstimator.Estimate(muscle, earlierStillApplies ? blocks : blocks.Where(b => b.PlanId == planId).ToList());
            var decision = LongitudinalVolumePlanner.Decide(baseline, from, response);
            if (decision.LearnedTarget is { } target) learned[baseline.Muscle] = target;
            decisions.Add(decision);
        }
        return new LongitudinalResult(learned, decisions);
    }
}
