namespace Trazo.Application.Planning;

/// <summary>Headline numbers of the progress tab. A "session" is one saved workout log with at least one completed set.</summary>
public sealed record ProgressSummary(
    int TotalSessions, int SessionsLast30Days, int SessionsThisWeek, int WeeklyGoal, int StreakWeeks, int TotalSets, decimal TotalVolumeKg);

/// <summary>One workout of one exercise: the heaviest load, the repetitions done with it and the estimated one-repetition maximum.</summary>
public sealed record ExercisePointView(DateOnly Date, decimal TopWeightKg, int RepsAtTop, decimal? EstimatedOneRepMaxKg, decimal VolumeKg, int Sets);

/// <summary>
/// The history of one exercise across all the user's plans. <see cref="NewRecord"/> is true when the latest workout beat
/// every earlier one in estimated one-repetition maximum.
/// </summary>
public sealed record ExerciseProgressView(
    string ExerciseId, string Name, int Sessions, DateOnly LastDate, decimal? BestWeightKg, decimal? BestEstimatedOneRepMaxKg,
    DateOnly? RecordDate, bool NewRecord, IReadOnlyList<ExercisePointView> Points);

/// <summary>
/// Effective sets of one muscle, week by week (aligned with <c>WeekStarts</c>), counted as everywhere else (<see cref="LoadMetrics"/>).
/// <c>SessionsByWeek</c> counts the workouts that really worked it; <c>PrescribedByWeek</c> is what a calendar plan asked of
/// each of those weeks (null where it asked nothing); <c>PrescribedPerSevenDays</c> is what the current block asks per seven
/// days; <c>Priority</c> is the user's own ("Emphasize" or "Maintain").
/// </summary>
public sealed record MuscleVolumeView(
    string Muscle, IReadOnlyList<decimal> SetsByWeek, IReadOnlyList<int>? SessionsByWeek = null,
    IReadOnlyList<decimal?>? PrescribedByWeek = null, decimal? PrescribedPerSevenDays = null, string? Priority = null);

/// <summary>What the active plan asks of the muscles: its dated sessions, the current block per seven days and the priorities.</summary>
public sealed record ProgressProgram(
    IReadOnlyList<(DateOnly Date, IReadOnlyList<DoseExercise> Exercises)> Dated, IReadOnlyList<MuscleDoseView> PerSevenDays,
    IReadOnlyDictionary<string, string> Priorities);

public sealed record ProgressView(
    ProgressSummary Summary, IReadOnlyList<DateOnly> WeekStarts, IReadOnlyList<MuscleVolumeView> Muscles,
    IReadOnlyList<ExerciseProgressView> Exercises);

public sealed record ProgressSetRow(
    string ExerciseId, Guid LogId, DateTimeOffset LoggedAt, decimal? WeightKg, int? Repetitions, int SetNumber = 0, int Segment = 0);

public sealed record ProgressExerciseInfo(string Name, IReadOnlyList<string> PrimaryMuscles, IReadOnlyList<string> EffectiveSecondaryMuscles);

/// <summary>Pure computation of the progress tab from the completed sets the user logged.</summary>
public static class ProgressCalculator
{
    public const int Weeks = 8;
    public const int MaximumPoints = 30;
    public const int MaximumExercises = 60;
    private const int OneRepMaxRepetitionLimit = 12;

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Epley estimate; only meaningful up to about twelve repetitions, so higher counts give no estimate.</summary>
    public static decimal? EstimatedOneRepMax(decimal weightKg, int repetitions)
    {
        if (weightKg <= 0 || repetitions < 1 || repetitions > OneRepMaxRepetitionLimit) return null;
        return Math.Round(repetitions == 1 ? weightKg : weightKg * (1 + repetitions / 30m), 1);
    }

    public static ProgressView Build(
        IReadOnlyList<ProgressSetRow> rows, IReadOnlyDictionary<string, ProgressExerciseInfo> info, TimeZoneInfo zone,
        DateOnly today, int weeklyGoal, ProgressProgram? program = null)
    {
        DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

        var sessions = rows.GroupBy(r => r.LogId).Select(g => Local(g.First().LoggedAt)).ToList();
        var thisWeek = WeekStart(today);
        var weeksWithSession = sessions.Select(WeekStart).ToHashSet();
        // The current week may simply not have had its workout yet, so the streak can also end last week.
        var cursor = weeksWithSession.Contains(thisWeek) ? thisWeek : thisWeek.AddDays(-7);
        var streak = 0;
        while (weeksWithSession.Contains(cursor)) { streak++; cursor = cursor.AddDays(-7); }
        var summary = new ProgressSummary(
            sessions.Count, sessions.Count(d => d > today.AddDays(-30) && d <= today), sessions.Count(d => WeekStart(d) == thisWeek),
            weeklyGoal, streak, rows.Count(r => r.Segment == 0),
            Math.Round(rows.Where(r => r.WeightKg is > 0 && r.Repetitions is > 0).Sum(r => r.WeightKg!.Value * r.Repetitions!.Value), 0));

        var weekStarts = Enumerable.Range(0, Weeks).Select(i => thisWeek.AddDays(-7 * (Weeks - 1 - i))).ToList();
        var muscles = Muscles(rows, info, weekStarts, Local, program);

        var exercises = rows.GroupBy(r => r.ExerciseId).Select(g => ExerciseProgress(g.Key, g.ToList(), info, Local))
            .OrderByDescending(e => e.LastDate).ThenBy(e => e.Name, StringComparer.Ordinal).Take(MaximumExercises).ToList();
        return new ProgressView(summary, weekStarts, muscles, exercises);
    }

    private static List<MuscleVolumeView> Muscles(
        IReadOnlyList<ProgressSetRow> rows, IReadOnlyDictionary<string, ProgressExerciseInfo> info, IReadOnlyList<DateOnly> weekStarts,
        Func<DateTimeOffset, DateOnly> local, ProgressProgram? program)
    {
        var sets = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        var sessions = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var workout in rows.GroupBy(r => r.LogId))
        {
            var index = Index(weekStarts, WeekStart(local(workout.First().LoggedAt)));
            if (index < 0) continue;
            // A set counts once, and its segments add a fixed part between all of them (see LoggedSets).
            var exercises = workout.Where(r => info.ContainsKey(r.ExerciseId)).GroupBy(r => r.ExerciseId).Select(g => new DoseExercise(
                g.Key, info[g.Key].PrimaryMuscles, info[g.Key].EffectiveSecondaryMuscles,
                g.Count(r => r.Segment == 0) + LoggedSets.SegmentsWeight * g.Where(r => r.Segment > 0).Select(r => r.SetNumber).Distinct().Count()));
            foreach (var (muscle, amount) in LoadMetrics.Effective(exercises))
            {
                if (!sets.TryGetValue(muscle, out var values))
                {
                    sets[muscle] = values = new decimal[Weeks];
                    sessions[muscle] = new int[Weeks];
                }
                values[index] += amount;
                if (amount >= LoadMetrics.MeaningfulSets) sessions[muscle][index]++;
            }
        }

        var prescribed = new Dictionary<string, decimal?[]>(StringComparer.Ordinal);
        foreach (var (date, exercises) in program?.Dated ?? [])
        {
            var index = Index(weekStarts, WeekStart(date));
            if (index < 0) continue;
            foreach (var (muscle, amount) in LoadMetrics.Effective(exercises))
            {
                if (!prescribed.TryGetValue(muscle, out var values)) prescribed[muscle] = values = new decimal?[Weeks];
                values[index] = (values[index] ?? 0m) + amount;
            }
        }
        var perSevenDays = (program?.PerSevenDays ?? []).ToDictionary(d => d.Muscle, d => d.Sets, StringComparer.Ordinal);

        // A muscle the plan asks for and nothing worked is shown too: that is the gap worth seeing.
        return sets.Keys.Union(prescribed.Keys).Union(perSevenDays.Keys)
            .Select(m => new MuscleVolumeView(
                m, sets.GetValueOrDefault(m) ?? new decimal[Weeks], sessions.GetValueOrDefault(m) ?? new int[Weeks],
                prescribed.GetValueOrDefault(m), perSevenDays.TryGetValue(m, out var asked) ? asked : null,
                program?.Priorities.GetValueOrDefault(m)))
            .OrderByDescending(m => m.SetsByWeek.Sum()).ThenByDescending(m => m.PrescribedPerSevenDays ?? 0m).ThenBy(m => m.Muscle, StringComparer.Ordinal)
            .ToList();
    }

    private static int Index(IReadOnlyList<DateOnly> weekStarts, DateOnly week)
    {
        for (var i = 0; i < weekStarts.Count; i++)
            if (weekStarts[i] == week) return i;
        return -1;
    }

    private static ExerciseProgressView ExerciseProgress(
        string exerciseId, List<ProgressSetRow> rows, IReadOnlyDictionary<string, ProgressExerciseInfo> info, Func<DateTimeOffset, DateOnly> local)
    {
        var points = rows.GroupBy(r => r.LogId).Select(g =>
        {
            // Records and estimates come from the main sets; the tonnage includes everything that was lifted.
            var main = g.Where(r => r.Segment == 0).ToList();
            var withLoad = main.Where(r => r.WeightKg is > 0).ToList();
            var top = withLoad.Count == 0 ? 0m : withLoad.Max(r => r.WeightKg!.Value);
            var repsAtTop = (withLoad.Count == 0 ? main : withLoad.Where(r => r.WeightKg == top)).Select(r => r.Repetitions ?? 0).DefaultIfEmpty(0).Max();
            var estimates = withLoad.Select(r => r.Repetitions is { } reps ? EstimatedOneRepMax(r.WeightKg!.Value, reps) : null)
                .Where(e => e is not null).Select(e => e!.Value).ToList();
            var volume = g.Where(r => r.WeightKg is > 0 && r.Repetitions is > 0).Sum(r => r.WeightKg!.Value * r.Repetitions!.Value);
            return new ExercisePointView(local(g.First().LoggedAt), top, repsAtTop, estimates.Count == 0 ? null : estimates.Max(), Math.Round(volume, 0), main.Count);
        }).OrderBy(p => p.Date).ToList();

        var withEstimate = points.Where(p => p.EstimatedOneRepMaxKg is not null).ToList();
        var best = withEstimate.Count == 0 ? (decimal?)null : withEstimate.Max(p => p.EstimatedOneRepMaxKg);
        var last = withEstimate.LastOrDefault();
        var beforeLast = withEstimate.Count > 1 ? withEstimate[..^1].Max(p => p.EstimatedOneRepMaxKg) : null;
        var newRecord = last is not null && last == points[^1] && beforeLast is not null && last.EstimatedOneRepMaxKg > beforeLast;
        var heaviest = points.Where(p => p.TopWeightKg > 0).Select(p => (decimal?)p.TopWeightKg).DefaultIfEmpty(null).Max();
        return new ExerciseProgressView(
            exerciseId, info.TryGetValue(exerciseId, out var i) ? i.Name : exerciseId, points.Count, points[^1].Date, heaviest, best,
            best is null ? null : withEstimate.Last(p => p.EstimatedOneRepMaxKg == best).Date, newRecord,
            points.Count > MaximumPoints ? points[^MaximumPoints..] : points);
    }
}
