namespace Trazo.Application.Planning;

/// <summary>The work done (or prescribed) on one exercise, reduced to what dose needs.</summary>
public sealed record DoseExercise(IReadOnlyList<string> PrimaryMuscles, IReadOnlyList<string> SecondaryMuscles, int Sets);

/// <summary>A workout that really happened: its instant and what was done.</summary>
public sealed record DoseWorkout(DateTimeOffset PerformedAt, IReadOnlyList<DoseExercise> Exercises);

/// <summary>Weighted sets of one muscle and in how many workouts (or sessions) it was meaningfully worked.</summary>
public sealed record MuscleDoseView(string Muscle, decimal Sets, decimal Exposures);

/// <summary>
/// The dose of training a muscle receives, measured separately from the structure of the plan. A plan week (or, in a sequence plan, a
/// rotation) is a group of sessions, not an amount of time: five sessions done in six days and the same five done in ten days have the
/// same volume per rotation and very different doses per seven days. These functions never use the period number as time; they read
/// instants and sets.
/// <list type="bullet">
/// <item><b>Volume per session</b>: the sets of one session (read from the prescription or the log).</item>
/// <item><b>Volume per rotation</b>: the sum over the sessions of one period (<c>WeekLoadView</c>).</item>
/// <item><b>Exposure</b>: a workout that works a muscle with at least <see cref="MeaningfulSets"/> weighted sets.</item>
/// <item><b>Dose per seven days</b>: the weighted sets and exposures received in a real window of seven days (<see cref="Dose"/>).</item>
/// <item><b>Accumulated dose</b>: the same over any interval, by passing the workouts of that interval.</item>
/// </list>
/// Weighting: a set counts 1 for each primary muscle and <see cref="SecondaryWeight"/> for each secondary one, the same convention the
/// recovery advice and the progress screen use. It is a product convention, not a measurement of stimulus.
/// </summary>
public static class LoadMetrics
{
    public const decimal SecondaryWeight = 0.5m;

    /// <summary>A muscle worked with fewer weighted sets than this in a session did not really receive a stimulus (same limit as recovery).</summary>
    public const decimal MeaningfulSets = 2m;

    /// <summary>Workouts of the last four weeks are the evidence for the observed pace; fewer than this many say nothing reliable.</summary>
    public const int PaceWindowDays = 28;
    public const int MinimumWorkoutsForPace = 4;
    public const int MinimumSpanDaysForPace = 5;

    /// <summary>Weighted sets per muscle of one session or workout.</summary>
    public static IReadOnlyDictionary<string, decimal> Weighted(IEnumerable<DoseExercise> exercises)
    {
        var total = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var exercise in exercises)
        {
            foreach (var muscle in exercise.PrimaryMuscles) total[muscle] = total.GetValueOrDefault(muscle) + exercise.Sets;
            foreach (var muscle in exercise.SecondaryMuscles.Except(exercise.PrimaryMuscles, StringComparer.Ordinal))
                total[muscle] = total.GetValueOrDefault(muscle) + exercise.Sets * SecondaryWeight;
        }
        return total;
    }

    /// <summary>Dose received over the given workouts: weighted sets and exposures per muscle, most worked first.</summary>
    public static IReadOnlyList<MuscleDoseView> Dose(IEnumerable<DoseWorkout> workouts) => Accumulate(workouts.Select(w => w.Exercises));

    /// <summary>
    /// What the program prescribes per seven days: the sessions of the given block, divided by the nominal weeks they span
    /// (<paramref name="nominalWeeks"/> = periods of the block, each period being the sessions the user planned for seven days).
    /// It is the prescription, not a forecast of what the user will do.
    /// </summary>
    public static IReadOnlyList<MuscleDoseView> PrescribedPerSevenDays(IEnumerable<IReadOnlyList<DoseExercise>> sessions, decimal nominalWeeks)
    {
        if (nominalWeeks <= 0m) return [];
        return Accumulate(sessions).Select(d => new MuscleDoseView(d.Muscle, Math.Round(d.Sets / nominalWeeks, 1), Math.Round(d.Exposures / nominalWeeks, 1))).ToList();
    }

    /// <summary>
    /// The pace at which the user has really been training: sessions per seven days, measured between the first and the last of the
    /// workouts of the last <see cref="PaceWindowDays"/> days. It is <c>null</c> when the evidence is not enough (fewer than
    /// <see cref="MinimumWorkoutsForPace"/> workouts or a span under <see cref="MinimumSpanDaysForPace"/> days): a number is never made up.
    /// It describes the recent past; it does not say that the user is still training.
    /// </summary>
    public static decimal? ObservedSessionsPerSevenDays(IReadOnlyCollection<DateTimeOffset> performedAt, DateTimeOffset now)
    {
        var recent = performedAt.Where(at => at <= now && at > now.AddDays(-PaceWindowDays)).OrderBy(at => at).ToList();
        if (recent.Count < MinimumWorkoutsForPace) return null;
        var spanDays = (recent[^1] - recent[0]).TotalDays;
        if (spanDays < MinimumSpanDaysForPace) return null;
        return Math.Round((decimal)((recent.Count - 1) / spanDays * 7), 1);
    }

    private static List<MuscleDoseView> Accumulate(IEnumerable<IReadOnlyList<DoseExercise>> sessions)
    {
        var sets = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var exposures = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var session in sessions)
            foreach (var (muscle, weighted) in Weighted(session))
            {
                sets[muscle] = sets.GetValueOrDefault(muscle) + weighted;
                if (weighted >= MeaningfulSets) exposures[muscle] = exposures.GetValueOrDefault(muscle) + 1;
            }
        return sets.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new MuscleDoseView(p.Key, p.Value, exposures.GetValueOrDefault(p.Key))).ToList();
    }
}
