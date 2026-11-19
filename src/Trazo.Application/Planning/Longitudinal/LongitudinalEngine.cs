using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>A weekly target a previous block of the plan really used for a muscle.</summary>
public sealed record AppliedTarget(int MesocycleSequence, string Muscle, decimal Sets);

public sealed record LongitudinalResult(
    IReadOnlyDictionary<TrainingMuscleGroup, decimal> LearnedTargets, IReadOnlyList<VolumeDecision> Decisions);

/// <summary>
/// The whole longitudinal decision for one new block, as a pure function of data already loaded. It reads only the finished blocks of
/// THIS plan: a new plan starts from its own baseline and never from decisions taken in another plan. The infrastructure only loads the
/// observations and stores the result; the tests run this very function.
/// </summary>
public static class LongitudinalEngine
{
    public static LongitudinalResult Propose(
        Guid planId,
        int slotSequence,
        IReadOnlyList<TrainingMuscleBaseline> baselines,
        IReadOnlyList<MuscleWeekObservation> observations,
        IReadOnlyList<AppliedTarget> applied)
    {
        var decisions = new List<VolumeDecision>();
        var learned = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var baseline in baselines)
        {
            var muscle = baseline.Muscle.ToString();
            var blocks = observations
                .Where(o => o.PlanId == planId && o.Muscle == muscle && o.MesocycleSequence < slotSequence)
                .GroupBy(o => o.MesocycleSequence).OrderBy(g => g.Key)
                .Select(g => new BlockEvidence(planId, g.Key, g.Any(o => o.IsDeload), g.OrderBy(o => o.Period).ToList()))
                .ToList();
            var previous = applied.Where(a => a.Muscle == muscle && a.MesocycleSequence < slotSequence)
                .OrderByDescending(a => a.MesocycleSequence).Select(a => (decimal?)a.Sets).FirstOrDefault();
            // The first block of a plan has nothing to read and no earlier target: it is the plain baseline.
            if (blocks.Count == 0 && previous is null) continue;
            var decision = LongitudinalVolumePlanner.Decide(baseline, previous, BlockAssessor.Summaries(blocks));
            if (decision.LearnedTarget is { } target) learned[baseline.Muscle] = target;
            decisions.Add(decision);
        }
        return new LongitudinalResult(learned, decisions);
    }
}
