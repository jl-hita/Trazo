using Trazo.Domain.Planning;

namespace Trazo.Application.Planning;

/// <summary>
/// The one reading of logged sets when they have segments (the parts of a drop set, rest-pause or myo-reps set that follow
/// the main one, see docs/prescription-techniques.md). Rows are never counted as sets anywhere else:
/// <list type="bullet">
/// <item><b>Performance</b> (progression, load references, last performance, records, indicators): the main sets only, so a
/// segment, lighter or shorter by design, can never read as a regression.</item>
/// <item><b>Volume</b>: a set counts 1, plus <see cref="SegmentsWeight"/> once if any of its segments was done. It is a
/// product convention: no validated equivalence exists.</item>
/// <item><b>Pain</b>: any row, main or segment.</item>
/// </list>
/// </summary>
public static class LoggedSets
{
    /// <summary>What all the segments of one set add together, however many they are.</summary>
    public const decimal SegmentsWeight = 0.5m;

    /// <summary>The main sets, without their segments.</summary>
    public static IEnumerable<WorkoutSetLog> Main(this IEnumerable<WorkoutSetLog> sets) => sets.Where(s => s.SegmentIndex == 0);

    public static bool HasPain(WorkoutSetLog set) =>
        set.PainReported || string.Equals(set.SkippedReason, "pain", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The effective sets done: every completed main set counts 1, and <see cref="SegmentsWeight"/> more when at least one of its
    /// segments was completed. The rows may be of several exercises of one workout.
    /// </summary>
    public static decimal EffectiveCount(IEnumerable<WorkoutSetLog> sets) =>
        sets.Where(s => s.Completed)
            .GroupBy(s => (Exercise: s.ExerciseId.ToLowerInvariant(), s.SetNumber))
            .Sum(set => set.Any(s => s.SegmentIndex == 0) ? 1m + (set.Any(s => s.SegmentIndex > 0) ? SegmentsWeight : 0m) : 0m);

    /// <summary>
    /// The reserve recovery should read for one exercise of a workout: the average of its main sets (null unless all of them say
    /// it), and no more than <see cref="TechniqueRir"/> when a segment was done, because a technique set ends close to failure.
    /// A product heuristic.
    /// </summary>
    public static decimal? RecoveryRir(IEnumerable<WorkoutSetLog> sets)
    {
        var done = sets.Where(s => s.Completed).ToList();
        var main = done.Main().ToList();
        var rirs = main.Where(s => s.Rir is not null).Select(s => s.Rir!.Value).ToList();
        var average = rirs.Count > 0 && rirs.Count == main.Count ? rirs.Average() : (decimal?)null;
        return done.Any(s => s.SegmentIndex > 0) ? Math.Min(average ?? TechniqueRir, TechniqueRir) : average;
    }

    public const decimal TechniqueRir = 1m;

    /// <summary>
    /// One record per main set, as the progression rules read them. Pain reported on a segment belongs to its set; why a segment
    /// stopped does not, because stopping is how a segment ends.
    /// </summary>
    public static SetRecord[] ProgressionRecords(IEnumerable<WorkoutSetLog> sets)
    {
        var all = sets.ToList();
        return all.Main().OrderBy(s => s.SetNumber).Select(main => new SetRecord(
                main.WeightKg, main.Repetitions, main.Rir, main.Completed, main.SkippedReason,
                all.Any(s => s.SetNumber == main.SetNumber && string.Equals(s.ExerciseId, main.ExerciseId, StringComparison.OrdinalIgnoreCase) && HasPain(s))))
            .ToArray();
    }
}
