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
    public static LongitudinalResult Propose(
        Guid planId,
        int slotSequence,
        IReadOnlyList<TrainingMuscleBaseline> baselines,
        IReadOnlyList<MuscleWeekObservation> observations,
        IReadOnlyDictionary<Guid, int> planOrder,
        IReadOnlyList<AppliedTarget> applied)
    {
        var decisions = new List<VolumeDecision>();
        var learned = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var baseline in baselines)
        {
            var muscle = baseline.Muscle.ToString();
            // Only blocks that are over: earlier blocks of this plan and every block of an earlier plan.
            var blocks = observations
                .Where(o => o.Muscle == muscle && (o.PlanId != planId || o.MesocycleSequence < slotSequence))
                .GroupBy(o => (o.PlanId, o.MesocycleSequence))
                .OrderBy(g => planOrder.GetValueOrDefault(g.Key.PlanId)).ThenBy(g => g.Key.MesocycleSequence)
                .Select(g => new BlockEvidence(g.Key.PlanId, g.Key.MesocycleSequence, g.Any(o => o.IsDeload), g.OrderBy(o => o.Period).ToList()))
                .ToList();
            var response = blocks.Count == 0 ? null : MuscleResponseEstimator.Estimate(muscle, blocks);
            var previous = applied.Where(a => a.Muscle == muscle && a.MesocycleSequence < slotSequence)
                .OrderByDescending(a => a.MesocycleSequence).Select(a => (decimal?)a.Sets).FirstOrDefault();
            // Nothing to learn from yet and nothing ever applied: the block uses the plan's own target and there is nothing to audit.
            if (response is null && previous is null) continue;
            var decision = LongitudinalVolumePlanner.Decide(baseline, previous, response);
            if (decision.LearnedTarget is { } target) learned[baseline.Muscle] = target;
            decisions.Add(decision);
        }
        return new LongitudinalResult(learned, decisions);
    }
}
