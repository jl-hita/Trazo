using Trazo.Training;

namespace Trazo.Application.Planning;

public enum PriorityDeliveryStatus
{
    /// <summary>The block holds about the weekly sets the priority asked for (inside the tolerance the allocator itself uses).</summary>
    Met,
    /// <summary>The block holds fewer sets than asked (session time, the safety ceilings or the exercises available did not allow more).</summary>
    Below,
    /// <summary>A muscle kept in maintenance holds more sets than asked: other exercises train it indirectly.</summary>
    Above
}

/// <summary>One muscle with a priority: what the plan aimed at and what the block really holds, in effective weekly sets.</summary>
public sealed record PriorityDeliveryItem(
    TrainingMuscleGroup Muscle, TrainingMusclePriority Priority, decimal TargetSets, decimal AssignedSets, PriorityDeliveryStatus Status);

/// <summary>
/// Compares the weekly target of each prioritised muscle with the sets the generated block really assigns, so the app never promises a
/// volume that was not programmed. It changes nothing: limits (session time, ceilings) are never relaxed to satisfy a priority, so a
/// shortfall is reported, not corrected. The tolerance is the one the allocator applies around an explicit target.
/// </summary>
public static class PriorityDelivery
{
    /// <param name="targets">The weekly target of each muscle for the block (the planner's baseline, or the learned reduction of that block).</param>
    /// <param name="assigned">Effective weekly sets the block assigns to each muscle (from its stored prescriptions).</param>
    public static IReadOnlyList<PriorityDeliveryItem> Build(
        IReadOnlyList<TrainingMuscleBaseline> baselines,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> assigned)
    {
        var tolerance = TrainingProgramVolumeAllocator.ExplicitHypertrophyTargetToleranceSets;
        var items = new List<PriorityDeliveryItem>();
        foreach (var baseline in baselines.Where(b => b.Priority != TrainingMusclePriority.Grow))
        {
            var target = targets.TryGetValue(baseline.Muscle, out var t) ? t : baseline.Target;
            var done = assigned.GetValueOrDefault(baseline.Muscle);
            var status = done < target - tolerance ? PriorityDeliveryStatus.Below
                : baseline.Priority == TrainingMusclePriority.Maintain && done > target + tolerance ? PriorityDeliveryStatus.Above
                : PriorityDeliveryStatus.Met;
            items.Add(new PriorityDeliveryItem(baseline.Muscle, baseline.Priority, target, done, status));
        }
        return items;
    }

    /// <summary>Effective weekly sets per muscle of the given sessions, counting indirect work the way the engine does everywhere else.</summary>
    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> WeeklySets(IEnumerable<IReadOnlyList<TrainingExercisePrescription>> sessions)
    {
        var total = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(s => s))
            foreach (var muscle in exercise.StimulatedMuscles())
                total[muscle] = total.GetValueOrDefault(muscle) + exercise.Sets;
        return total;
    }
}
