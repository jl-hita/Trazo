using System.Text.Json;
using Trazo.Training;

namespace Trazo.Application.Planning.Frequency;

public sealed record MusclePreview(string Muscle, decimal CurrentSets, decimal ProposedSets, int CurrentFrequency, int ProposedFrequency);

/// <summary>What the next block would look like with the proposed days against the current ones, from the real generator (one typical week).</summary>
public sealed record FrequencyPreview(
    int CurrentSessionsPerWeek, int ProposedSessionsPerWeek, int CurrentLongestSessionMinutes, int ProposedLongestSessionMinutes,
    IReadOnlyList<MusclePreview> Muscles, IReadOnlyList<string> Warnings);

public sealed record FrequencyDraft(
    string Kind, IReadOnlyList<DayOfWeek> ToDays, string? ToSplit, FrequencyAssessment Assessment, FrequencyPreview Preview, string Reason)
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="profile">The profile the plan has now.</param>
    /// <param name="originalDays">Days before an accepted reduction (to return to them), or null.</param>
    /// <param name="slot">The next block to generate: the one a proposal would apply to.</param>
    public static FrequencyDraft? Propose(
        PlanProfile profile, FrequencyAssessment assessment, IReadOnlyList<WeeklyAttendance> weeks, IReadOnlyList<DayOfWeek>? originalDays,
        int seed, MesocycleSlot slot, IReadOnlyCollection<TrainingExerciseCandidate> catalog, IReadOnlyList<string>? previousIds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (assessment.TargetDays is not { } target) return null;
        if (assessment.Pattern is not (FrequencyPattern.PersistentReduction or FrequencyPattern.PersistentReturn)) return null;
        if (profile.SchedulingMode != Trazo.Domain.Planning.PlanSchedulingMode.Calendar) return null;

        var isReturn = assessment.Pattern == FrequencyPattern.PersistentReturn;
        var days = isReturn && originalDays is { } original && original.Count == target
            ? original.OrderBy(FrequencyPatternDetector.Order).ToList()
            : ChooseDays(target, weeks, profile.TrainingDays, isReturn ? originalDays : null);
        if (days.Count != target) return null;

        foreach (var split in new[] { profile.Split, TrainingProgramSplit.Automatic }.Distinct())
        {
            var proposed = profile with { TrainingDays = days, Split = split };
            var preview = Preview(profile, proposed, seed, slot, catalog, previousIds);
            if (preview is null) continue;
            // Never longer sessions to make up for fewer days: a block that needs more than the plan's maximum is not a proposal.
            if (preview.ProposedLongestSessionMinutes > profile.MaxSessionMinutes) continue;
            return new FrequencyDraft(
                isReturn ? FrequencyDraft.Restore : FrequencyDraft.Reduce, days, split == profile.Split ? null : split.ToString(),
                assessment, preview, ReasonOf(assessment, profile, days, isReturn, split != profile.Split));
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

    /// <summary>The same block generated for the current and for the proposed days, read one week in; null when the proposed one cannot be generated.</summary>
    public static FrequencyPreview? Preview(
        PlanProfile current, PlanProfile proposed, int seed, MesocycleSlot slot,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog, IReadOnlyList<string>? previousIds)
    {
        var now = MesocycleGenerator.Generate(current, seed, slot, catalog, previousIds);
        var next = MesocycleGenerator.Generate(proposed, seed, slot, catalog, previousIds);
        if (!next.Succeeded || next.Quality is null) return null;
        var (nowSessions, nowMinutes, nowVolumes) = Week(now);
        var (nextSessions, nextMinutes, nextVolumes) = Week(next);
        var muscles = nextVolumes.Keys.Union(nowVolumes.Keys).OrderBy(m => m.ToString(), StringComparer.Ordinal)
            .Select(m =>
            {
                var a = nowVolumes.GetValueOrDefault(m);
                var b = nextVolumes.GetValueOrDefault(m);
                return new MusclePreview(m.ToString(), Math.Round(a.Sets, 1), Math.Round(b.Sets, 1), a.Frequency, b.Frequency);
            })
            .Where(m => m.CurrentSets > 0 || m.ProposedSets > 0).ToList();
        return new FrequencyPreview(nowSessions, nextSessions, nowMinutes, nextMinutes, muscles, next.Quality.Warnings);
    }

    private static (int Sessions, int Longest, Dictionary<TrainingMuscleGroup, (decimal Sets, int Frequency)> Volumes) Week(MesocycleGeneration g)
    {
        var volumes = new Dictionary<TrainingMuscleGroup, (decimal, int)>();
        if (!g.Succeeded || g.Quality is null) return (0, 0, volumes);
        var weekNumber = g.Sessions.Select(s => s.WeekNumber).Distinct().OrderBy(w => w).Skip(1).FirstOrDefault(g.Sessions.Min(s => s.WeekNumber));
        var sessions = g.Sessions.Where(s => s.WeekNumber == weekNumber).ToList();
        foreach (var m in g.Quality.WeeklyMuscleVolumes.First(w => w.WeekNumber == weekNumber).Muscles)
            volumes[m.MuscleGroup] = (m.EstimatedEffectiveSets, m.PrimaryFrequencySessions);
        return (sessions.Count, sessions.Count == 0 ? 0 : sessions.Max(s => s.EstimatedDurationMinutes), volumes);
    }

    public static string DaysEs(IEnumerable<DayOfWeek> days) =>
        string.Join(", ", days.OrderBy(FrequencyPatternDetector.Order).Select(d => d switch
        {
            DayOfWeek.Monday => "lunes", DayOfWeek.Tuesday => "martes", DayOfWeek.Wednesday => "miércoles", DayOfWeek.Thursday => "jueves",
            DayOfWeek.Friday => "viernes", DayOfWeek.Saturday => "sábado", _ => "domingo"
        }));

    private static string ReasonOf(FrequencyAssessment a, PlanProfile profile, IReadOnlyList<DayOfWeek> days, bool isReturn, bool splitChanged)
    {
        var text = isReturn
            ? $"{a.Reason} Se propone volver a {days.Count} días ({DaysEs(days)}) a partir del próximo bloque."
            : $"{a.Reason} Se propone un plan de {days.Count} días ({DaysEs(days)}) a partir del próximo bloque, con el volumen semanal de cada músculo ajustado a esos días.";
        text += " Lo que no se entrenó no se acumula ni se recupera, las sesiones no se alargan, se respetan tus prioridades y restricciones y el bloque actual no se modifica.";
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
