namespace Trazo.Application.Planning;

/// <summary>Effective sets of one muscle in a block: what the plan asked for against what was done.</summary>
public sealed record BlockMuscleView(string Muscle, decimal PrescribedSets, decimal PerformedSets);

/// <summary>
/// One exercise done both in this block and in the previous training block: the best estimated one-repetition maximum of each
/// and the change between them. It is an estimate from the sets logged, not a measurement of muscle.
/// </summary>
public sealed record BlockExerciseView(
    string ExerciseId, string Name, decimal BestEstimatedOneRepMaxKg, decimal PreviousBestEstimatedOneRepMaxKg, decimal ChangePercent);

public sealed record BlockSummaryView(
    int Sequence, string PhaseType, bool IsDeload, int StartWeek, int Weeks, int SessionsPlanned, int SessionsDone,
    IReadOnlyList<BlockMuscleView> Muscles, IReadOnlyList<BlockExerciseView> Exercises);

public sealed record BlockSummariesView(Guid PlanId, IReadOnlyList<BlockSummaryView> Blocks);

/// <summary>What one block holds: its place, the sessions prescribed and the workouts logged against them.</summary>
public sealed record BlockFacts(
    int Sequence, string PhaseType, bool IsDeload, int StartWeek, int Weeks,
    IReadOnlyList<IReadOnlyList<DoseExercise>> Prescribed, IReadOnlyList<IReadOnlyList<DoseExercise>> Performed,
    IReadOnlyList<ProgressSetRow> Sets);

/// <summary>
/// Compares blocks honestly (docs/progress-statistics.md): loads only on exercises both blocks share, only between training
/// blocks (a deload or a preparation is lighter on purpose), and only from sets that allow an estimate.
/// </summary>
public static class BlockSummaries
{
    public static bool IsTraining(BlockFacts block) => !block.IsDeload && block.PhaseType != PreparationPhase.PhaseType;

    public static IReadOnlyList<BlockSummaryView> Build(IReadOnlyList<BlockFacts> blocks, Func<string, string> nameOf)
    {
        var result = new List<BlockSummaryView>(blocks.Count);
        IReadOnlyDictionary<string, decimal>? previous = null;
        foreach (var block in blocks.OrderBy(b => b.Sequence))
        {
            var prescribed = Total(block.Prescribed);
            var performed = Total(block.Performed);
            var muscles = prescribed.Keys.Union(performed.Keys)
                .Select(m => new BlockMuscleView(m, prescribed.GetValueOrDefault(m), performed.GetValueOrDefault(m)))
                .OrderByDescending(m => m.PrescribedSets).ThenBy(m => m.Muscle, StringComparer.Ordinal).ToList();
            var best = Best(block.Sets);
            var exercises = IsTraining(block) && previous is not null
                ? best.Where(b => previous.ContainsKey(b.Key))
                    .Select(b => new BlockExerciseView(b.Key, nameOf(b.Key), b.Value, previous[b.Key],
                        Math.Round((b.Value - previous[b.Key]) / previous[b.Key] * 100, 1)))
                    .OrderBy(e => e.Name, StringComparer.Ordinal).ToList()
                : [];
            result.Add(new BlockSummaryView(block.Sequence, block.PhaseType, block.IsDeload, block.StartWeek, block.Weeks,
                block.Prescribed.Count, block.Performed.Count, muscles, exercises));
            if (IsTraining(block) && best.Count > 0) previous = best;
        }
        return result;
    }

    private static Dictionary<string, decimal> Total(IEnumerable<IReadOnlyList<DoseExercise>> sessions)
    {
        var total = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var session in sessions)
            foreach (var (muscle, sets) in LoadMetrics.Effective(session))
                total[muscle] = total.GetValueOrDefault(muscle) + sets;
        return total;
    }

    private static Dictionary<string, decimal> Best(IEnumerable<ProgressSetRow> rows) =>
        rows.Where(r => r.Segment == 0 && r.WeightKg is > 0 && r.Repetitions is not null)
            .Select(r => (r.ExerciseId, Estimate: ProgressCalculator.EstimatedOneRepMax(r.WeightKg!.Value, r.Repetitions!.Value)))
            .Where(x => x.Estimate is > 0)
            .GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Estimate!.Value), StringComparer.OrdinalIgnoreCase);
}
