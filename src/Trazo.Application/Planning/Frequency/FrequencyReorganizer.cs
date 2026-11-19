using System.Text.Json;
using Trazo.Training;

namespace Trazo.Application.Planning.Frequency;

public sealed record MusclePreview(string Muscle, decimal CurrentSets, decimal ProposedSets, int CurrentFrequency, int ProposedFrequency);

/// <summary>What the next block would look like with the proposed days against the current ones, from the real generator (one typical week).</summary>
public sealed record FrequencyPreview(
    int CurrentSessionsPerWeek, int ProposedSessionsPerWeek, int CurrentLongestSessionMinutes, int ProposedLongestSessionMinutes,
    IReadOnlyList<MusclePreview> Muscles, IReadOnlyList<string> Warnings,
    int CurrentAverageSessionMinutes = 0, int ProposedAverageSessionMinutes = 0, int ProposedMaxSessionMinutes = 0);

public sealed record FrequencyDraft(
    string Kind, IReadOnlyList<DayOfWeek> ToDays, string? ToSplit, FrequencyAssessment Assessment, FrequencyPreview Preview, string Reason,
    int? ToMaxSessionMinutes = null)
{
    public const string Reduce = "reduce";
    public const string Restore = "restore";
}

/// <summary>
/// Turns a persistent change of availability into a concrete proposal: which days, validated by generating the next block with the real
/// generator. It never rewrites anything. It does not accumulate or recover what was not trained (the new block is generated for the new
/// days from their own weekly target), it does not stretch sessions (a block whose sessions exceed the plan's maximum is rejected) and it
/// keeps what the generator already respects: muscle priorities, recovery between sessions and the user's restrictions (equipment,
/// excluded exercises, limitations travel in the profile). Pure and deterministic.
/// </summary>
public static class FrequencyReorganizer
{
    /// <summary>A reorganised plan may have sessions this much longer on average than the current ones (product limit; the plan's own maximum still holds).</summary>
    public const decimal LongerSessionAllowance = 1.10m;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="profile">The profile the plan has now.</param>
    /// <param name="originalDays">Days before an accepted reduction (to return to them), or null.</param>
    /// <param name="originalMaxSessionMinutes">Longest session the plan allowed before an accepted reduction, to go back to it, or null.</param>
    /// <param name="slot">The next block to generate: the one a proposal would apply to.</param>
    public static FrequencyDraft? Propose(
        PlanProfile profile, FrequencyAssessment assessment, IReadOnlyList<WeeklyAttendance> weeks, IReadOnlyList<DayOfWeek>? originalDays,
        int seed, MesocycleSlot slot, IReadOnlyCollection<TrainingExerciseCandidate> catalog, IReadOnlyList<string>? previousIds,
        int? originalMaxSessionMinutes = null, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (assessment.TargetDays is not { } target) return null;
        if (assessment.Pattern is not (FrequencyPattern.PersistentReduction or FrequencyPattern.PersistentReturn)) return null;
        if (profile.SchedulingMode != Trazo.Domain.Planning.PlanSchedulingMode.Calendar) return null;

        var isReturn = assessment.Pattern == FrequencyPattern.PersistentReturn;
        var days = isReturn && originalDays is { } original && original.Count == target
            ? original.OrderBy(FrequencyPatternDetector.Order).ToList()
            : ChooseDays(target, weeks, profile.TrainingDays, isReturn ? originalDays : null);
        trace?.Invoke($"days={days.Count}/{target}");
        if (days.Count != target) return null;

        foreach (var split in new[] { profile.Split, TrainingProgramSplit.Automatic }.Distinct())
        {
            // The session length is kept, not stretched: fewer days never mean longer sessions. A reduction keeps the sessions about as long as
            // the current ones (a little room), so the weekly volume follows the time the user really has; going back restores the old limit.
            var maxMinutes = isReturn
                ? Math.Max(profile.MaxSessionMinutes, originalMaxSessionMinutes ?? profile.MaxSessionMinutes)
                : CappedMinutes(profile, split, seed, slot, catalog, previousIds);
            var proposed = profile with { TrainingDays = days, Split = split, MaxSessionMinutes = maxMinutes };
            var preview = Preview(profile, proposed, seed, slot, catalog, previousIds);
            trace?.Invoke($"split={split} max={maxMinutes} preview={(preview is null ? "null" : $"long={preview.ProposedLongestSessionMinutes} avg={preview.ProposedAverageSessionMinutes} curavg={preview.CurrentAverageSessionMinutes}")}");
            if (preview is null) continue;
            if (preview.ProposedLongestSessionMinutes > maxMinutes) continue;
            if (!isReturn && preview.ProposedAverageSessionMinutes > preview.CurrentAverageSessionMinutes * LongerSessionAllowance) continue;
            return new FrequencyDraft(
                isReturn ? FrequencyDraft.Restore : FrequencyDraft.Reduce, days, split == profile.Split ? null : split.ToString(),
                assessment, preview, ReasonOf(assessment, profile, days, isReturn, split != profile.Split, maxMinutes),
                maxMinutes == profile.MaxSessionMinutes ? null : maxMinutes);
        }
        return null;
    }

    /// <summary>
    /// The days to propose: the weekdays the user really trains on, but never three consecutive days for three days or fewer. Among the
    /// combinations of <paramref name="target"/> days it takes (1) the allowed ones, (2) the most trained weekdays, (3) the widest rest between
    /// sessions, (4) the ones already in the plan (or the original plan), (5) the earliest in the week. Deterministic.
    /// </summary>
    public static List<DayOfWeek> ChooseDays(
        int target, IReadOnlyList<WeeklyAttendance> weeks, IReadOnlyList<DayOfWeek> planDays, IReadOnlyList<DayOfWeek>? preferred)
    {
        var week = Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + 1) % 7)).ToArray(); // Monday .. Sunday
        var usage = week.ToDictionary(d => d, d => weeks.TakeLast(FrequencyPolicy.RecentWeeks).Count(w => !w.Deload && w.Days.Contains(d)));
        var inPlan = new HashSet<DayOfWeek>(preferred ?? planDays);
        (bool Ok, int Usage, int Gap, int Overlap, int Early)? bestScore = null;
        List<DayOfWeek>? best = null;
        foreach (var combo in Combinations(week, target))
        {
            var positions = combo.Select(FrequencyPatternDetector.Order).OrderBy(x => x).ToList();
            var ok = target > 3 || !HasRun(positions, 3);
            var gap = MinimumGap(positions);
            var score = (ok, combo.Sum(d => usage[d]), gap, combo.Count(inPlan.Contains), -positions.Sum());
            if (bestScore is null || Compare(score, bestScore.Value) > 0) { bestScore = score; best = [.. combo]; }
        }
        return best?.OrderBy(FrequencyPatternDetector.Order).ToList() ?? [];
    }

    private static int Compare((bool Ok, int Usage, int Gap, int Overlap, int Early) a, (bool Ok, int Usage, int Gap, int Overlap, int Early) b)
    {
        var c = a.Ok.CompareTo(b.Ok);
        if (c != 0) return c;
        c = a.Usage.CompareTo(b.Usage);
        if (c != 0) return c;
        c = a.Gap.CompareTo(b.Gap);
        if (c != 0) return c;
        c = a.Overlap.CompareTo(b.Overlap);
        return c != 0 ? c : a.Early.CompareTo(b.Early);
    }

    private static bool HasRun(List<int> sorted, int length)
    {
        var run = 1;
        for (var i = 1; i < sorted.Count; i++)
        {
            run = sorted[i] == sorted[i - 1] + 1 ? run + 1 : 1;
            if (run >= length) return true;
        }
        return false;
    }

    /// <summary>Smallest number of days between two consecutive sessions, counting the wrap from Sunday to Monday.</summary>
    private static int MinimumGap(List<int> sorted)
    {
        var min = int.MaxValue;
        for (var i = 0; i < sorted.Count; i++)
        {
            var next = sorted[(i + 1) % sorted.Count] + (i + 1 == sorted.Count ? 7 : 0);
            min = Math.Min(min, next - sorted[i]);
        }
        return sorted.Count < 2 ? 7 : min;
    }

    private static IEnumerable<List<DayOfWeek>> Combinations(DayOfWeek[] items, int k)
    {
        var idx = new int[k];
        for (var i = 0; i < k; i++) idx[i] = i;
        while (true)
        {
            yield return idx.Select(i => items[i]).ToList();
            var pos = k - 1;
            while (pos >= 0 && idx[pos] == items.Length - k + pos) pos--;
            if (pos < 0) yield break;
            idx[pos]++;
            for (var j = pos + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }

    /// <summary>The longest session the reorganised plan may have: the current average with a little room, rounded up to 5 minutes, never above the plan's maximum.</summary>
    private static int CappedMinutes(
        PlanProfile profile, TrainingProgramSplit split, int seed, MesocycleSlot slot,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog, IReadOnlyList<string>? previousIds)
    {
        var current = MesocycleGenerator.Generate(profile, seed, slot, catalog, previousIds);
        if (!current.Succeeded) return profile.MaxSessionMinutes;
        var (sessions, _, _, average) = WeekShape(current);
        if (sessions == 0 || average <= 0) return profile.MaxSessionMinutes;
        // Rounded down to five minutes (rounding up could allow more than the allowance), but never below the sessions the user has now.
        var capped = Math.Max((int)(Math.Floor(average * LongerSessionAllowance / 5m) * 5m), average);
        return Math.Min(profile.MaxSessionMinutes, Math.Max(30, capped));
    }

    private static (int Sessions, int Longest, Dictionary<TrainingMuscleGroup, (decimal Sets, int Frequency)> Volumes, int Average) WeekShape(MesocycleGeneration g)
    {
        var (sessions, longest, volumes) = Week(g);
        var sessionWeeks = g.Sessions.Select(s => s.WeekNumber).Distinct().OrderBy(w => w).ToList();
        var typical = sessionWeeks.Count == 0 ? [] : g.Sessions.Where(s => s.WeekNumber == sessionWeeks[Math.Min(1, sessionWeeks.Count - 1)]).ToList();
        return (sessions, longest, volumes, typical.Count == 0 ? 0 : (int)Math.Round(typical.Average(s => s.EstimatedDurationMinutes)));
    }

    /// <summary>The same block generated for the current and for the proposed days, read one week in; null when the proposed one cannot be generated.</summary>
    public static FrequencyPreview? Preview(
        PlanProfile current, PlanProfile proposed, int seed, MesocycleSlot slot,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog, IReadOnlyList<string>? previousIds)
    {
        var now = MesocycleGenerator.Generate(current, seed, slot, catalog, previousIds);
        var next = MesocycleGenerator.Generate(proposed, seed, slot, catalog, previousIds);
        if (!next.Succeeded || next.Quality is null) return null;
        var (nowSessions, nowMinutes, nowVolumes, nowAverage) = WeekShape(now);
        var (nextSessions, nextMinutes, nextVolumes, nextAverage) = WeekShape(next);
        var muscles = nextVolumes.Keys.Union(nowVolumes.Keys).OrderBy(m => m.ToString(), StringComparer.Ordinal)
            .Select(m =>
            {
                var a = nowVolumes.GetValueOrDefault(m);
                var b = nextVolumes.GetValueOrDefault(m);
                return new MusclePreview(m.ToString(), Math.Round(a.Sets, 1), Math.Round(b.Sets, 1), a.Frequency, b.Frequency);
            })
            .Where(m => m.CurrentSets > 0 || m.ProposedSets > 0).ToList();
        return new FrequencyPreview(nowSessions, nextSessions, nowMinutes, nextMinutes, muscles, next.Quality.Warnings, nowAverage, nextAverage, proposed.MaxSessionMinutes);
    }

    private static (int Sessions, int Longest, Dictionary<TrainingMuscleGroup, (decimal Sets, int Frequency)> Volumes) Week(MesocycleGeneration g)
    {
        var volumes = new Dictionary<TrainingMuscleGroup, (decimal, int)>();
        if (!g.Succeeded || g.Quality is null) return (0, 0, volumes);
        // The quality report numbers weeks inside the block and the sessions carry the plan's absolute week: both are read by position (the second week).
        var sessionWeeks = g.Sessions.Select(s => s.WeekNumber).Distinct().OrderBy(w => w).ToList();
        var sessions = g.Sessions.Where(s => s.WeekNumber == sessionWeeks[Math.Min(1, sessionWeeks.Count - 1)]).ToList();
        var qualityWeeks = g.Quality.WeeklyMuscleVolumes.OrderBy(w => w.WeekNumber).ToList();
        foreach (var m in qualityWeeks[Math.Min(1, qualityWeeks.Count - 1)].Muscles)
            volumes[m.MuscleGroup] = (m.EstimatedEffectiveSets, m.PrimaryFrequencySessions);
        return (sessions.Count, sessions.Count == 0 ? 0 : sessions.Max(s => s.EstimatedDurationMinutes), volumes);
    }

    public static string DaysEs(IEnumerable<DayOfWeek> days) =>
        string.Join(", ", days.OrderBy(FrequencyPatternDetector.Order).Select(d => d switch
        {
            DayOfWeek.Monday => "lunes", DayOfWeek.Tuesday => "martes", DayOfWeek.Wednesday => "miércoles", DayOfWeek.Thursday => "jueves",
            DayOfWeek.Friday => "viernes", DayOfWeek.Saturday => "sábado", _ => "domingo"
        }));

    private static string ReasonOf(FrequencyAssessment a, PlanProfile profile, IReadOnlyList<DayOfWeek> days, bool isReturn, bool splitChanged, int maxMinutes)
    {
        var text = isReturn
            ? $"{a.Reason} Se propone volver a {days.Count} días ({DaysEs(days)}) a partir del próximo bloque."
            : $"{a.Reason} Se propone un plan de {days.Count} días ({DaysEs(days)}) a partir del próximo bloque, con el volumen semanal de cada músculo ajustado a esos días.";
        text += " Lo que no se entrenó no se acumula ni se recupera, las sesiones no se alargan, se respetan tus prioridades y restricciones y el bloque actual no se modifica.";
        if (maxMinutes != profile.MaxSessionMinutes)
            text += isReturn
                ? $" La duración máxima de sesión vuelve a {maxMinutes} min."
                : $" Las sesiones no se alargan: la duración máxima pasa de {profile.MaxSessionMinutes} a {maxMinutes} min, como tus sesiones actuales, y el volumen semanal se ajusta al tiempo real que tienes.";
        if (splitChanged) text += " La distribución pasa a automática porque la que tenías no admite esos días.";
        return text;
    }

    public static string PreviewJson(FrequencyPreview preview) => JsonSerializer.Serialize(preview, Json);

    public static FrequencyPreview? ReadPreview(string json)
    {
        try { return JsonSerializer.Deserialize<FrequencyPreview>(json, Json); }
        catch (JsonException) { return null; }
    }
}
