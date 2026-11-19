namespace Trazo.Application.Planning.Frequency;

/// <summary>One complete week of a plan (seven days from its start date): the days the user really trained.</summary>
public sealed record WeeklyAttendance(DateOnly WeekStart, IReadOnlyList<DayOfWeek> Days, bool Deload)
{
    public int Sessions => Days.Count;
}

public enum FrequencyPattern
{
    /// <summary>Not enough readable weeks (too few, deloads, absences) to say anything.</summary>
    Insufficient,
    OnTrack,
    /// <summary>One or two weeks below the plan: an irregular week, not a change of availability.</summary>
    Transient,
    /// <summary>The user keeps training fewer days than planned, week after week.</summary>
    PersistentReduction,
    /// <summary>After a reorganisation, the user keeps training the original number of days again.</summary>
    PersistentReturn
}

/// <param name="Observed">Sessions per week of the latest weeks that count (not absent, not deload); null when there are none.</param>
/// <param name="TargetDays">Days per week a proposal would use (reduction or return); null otherwise.</param>
public sealed record FrequencyAssessment(
    FrequencyPattern Pattern, int NominalDays, int? TargetDays, int WeeksAnalysed, int LowWeeks, string Reason, decimal? Observed = null);

/// <summary>
/// Tells a one-off irregular week from a persistent change of the days the user really trains. Pure and deterministic.
/// What it reads: the distinct days with a workout in each complete week. What it never counts as evidence: weeks without any workout
/// (an absence says nothing about availability), deload weeks (training less is the plan), the week in progress.
/// </summary>
public static class FrequencyPatternDetector
{
    /// <summary>
    /// Builds the complete plan weeks (seven days counted from <paramref name="anchor"/>, the plan's start date) between <paramref name="since"/>
    /// and the week before <paramref name="today"/>, oldest first.
    /// </summary>
    public static IReadOnlyList<WeeklyAttendance> Weeks(
        IEnumerable<DateOnly> workoutDays, DateOnly today, DateOnly since, DateOnly anchor, Func<DateOnly, bool> isDeloadWeek)
    {
        int Index(DateOnly d) => (int)Math.Floor((d.DayNumber - anchor.DayNumber) / 7.0);
        var byWeek = workoutDays.Distinct().Where(d => d >= anchor).GroupBy(Index)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DayOfWeek).Distinct().OrderBy(Order).ToList());
        var weeks = new List<WeeklyAttendance>();
        var first = Math.Max(0, (int)Math.Ceiling((since.DayNumber - anchor.DayNumber) / 7.0));
        for (var i = first; anchor.AddDays(7 * i + 6) < today; i++)
        {
            var start = anchor.AddDays(7 * i);
            weeks.Add(new WeeklyAttendance(start, byWeek.TryGetValue(i, out var days) ? days : [], isDeloadWeek(start)));
        }
        return weeks;
    }

    /// <param name="nominalDays">Days per week the plan is built for today.</param>
    /// <param name="originalDays">Days per week before an accepted reduction, when there is one (so a return can be recognised); otherwise null.</param>
    public static FrequencyAssessment Assess(int nominalDays, int? originalDays, IReadOnlyList<WeeklyAttendance> weeks)
    {
        var window = weeks.TakeLast(FrequencyPolicy.WindowWeeks).ToList();
        var absent = window.Count(w => !w.Deload && w.Sessions == 0);
        var eligible = window.Where(w => !w.Deload && w.Sessions > 0).ToList();
        FrequencyAssessment Result(FrequencyPattern pattern, int? target, int low, string reason) =>
            new(pattern, nominalDays, target, eligible.Count, low, reason,
                eligible.Count == 0 ? null : Math.Round((decimal)eligible.TakeLast(FrequencyPolicy.RecentWeeks).Average(w => w.Sessions), 1));

        if (nominalDays <= 0) return Result(FrequencyPattern.Insufficient, null, 0, "El plan no tiene días fijos.");
        if (absent > FrequencyPolicy.MaximumAbsentWeeks)
            return Result(FrequencyPattern.Insufficient, null, 0, "Hay varias semanas sin entrenar: una ausencia no indica un cambio de disponibilidad.");
        if (eligible.Count < FrequencyPolicy.MinimumEligibleWeeks)
            return Result(FrequencyPattern.Insufficient, null, 0, "Aún no hay suficientes semanas completas para medir tu ritmo.");

        var recent = eligible.TakeLast(FrequencyPolicy.RecentWeeks).ToList();
        var low = recent.Where(w => w.Sessions <= nominalDays - 1).ToList();

        // Back to the original frequency after a reorganisation: the latest weeks all above the current days.
        if (originalDays is { } original && original > nominalDays)
        {
            var latest = eligible.TakeLast(FrequencyPolicy.ReturnWeeks).ToList();
            if (latest.Count == FrequencyPolicy.ReturnWeeks && latest.All(w => w.Sessions >= nominalDays + 1))
            {
                var target = Math.Min(original, latest.Min(w => w.Sessions));
                return Result(FrequencyPattern.PersistentReturn, target, 0,
                    $"Las últimas {FrequencyPolicy.ReturnWeeks} semanas has entrenado {target} o más días: has vuelto a tu frecuencia habitual.");
            }
        }

        if (low.Count == 0) return Result(FrequencyPattern.OnTrack, null, 0, "Tu ritmo real coincide con el del plan.");

        var latestLow = recent.TakeLast(FrequencyPolicy.LatestWeeksMustBeLow).All(w => w.Sessions <= nominalDays - 1);
        var spread = low.Max(w => w.Sessions) - low.Min(w => w.Sessions);
        if (low.Count >= FrequencyPolicy.LowWeeksForPersistence && latestLow && spread <= FrequencyPolicy.MaximumSpreadOfLowWeeks)
        {
            var ordered = low.Select(w => w.Sessions).OrderBy(x => x).ToList();
            var typical = ordered[ordered.Count / 2]; // median of the low weeks, lower middle for an even count
            typical = Math.Max(FrequencyPolicy.MinimumDaysPerWeek, Math.Min(nominalDays - 1, typical));
            return Result(FrequencyPattern.PersistentReduction, typical, low.Count,
                $"En {low.Count} de las últimas {recent.Count} semanas has entrenado {ordered.Min()}-{ordered.Max()} días en lugar de {nominalDays}, y las dos últimas también: no es algo puntual.");
        }
        return Result(FrequencyPattern.Transient, null, low.Count,
            $"{low.Count} de las últimas {recent.Count} semanas por debajo de los {nominalDays} días: se trata como una semana irregular, no como un cambio.");
    }

    /// <summary>Monday first, so a week reads Monday to Sunday.</summary>
    public static int Order(DayOfWeek day) => ((int)day + 6) % 7;
}
