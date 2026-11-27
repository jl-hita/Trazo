namespace Trazo.Application.Planning;

/// <summary>
/// One thing worth looking at, always in the same shape: what, why (with its figures), what could be done and how sure it is.
/// Nothing is applied: <c>Action</c> names what the user can choose to do ("swap" an exercise of <c>SessionId</c>, or
/// "adjust_plan"). <c>Confidence</c> is "low" or "medium"; no rule here is certain.
/// </summary>
public sealed record RecommendationView(
    string Kind, string Title, string Why, string Suggestion, string Confidence, string? Action,
    string? ExerciseId = null, string? ExerciseName = null, Guid? SessionId = null,
    IReadOnlyDictionary<string, decimal>? Data = null);

/// <summary><c>Locked</c>: recommendations are part of Premium, like the other automatic help.</summary>
public sealed record RecommendationsView(IReadOnlyList<RecommendationView> Items, bool Locked = false);

/// <summary>One time an exercise was done: when, its best estimate, whether pain was reported and whether the block was a training one.</summary>
public sealed record ExerciseExposure(string ExerciseId, DateTimeOffset At, decimal? EstimatedOneRepMaxKg, bool Pain, bool Training);

/// <summary>The effective sets asked and done over the last <see cref="Recommendations.AdherenceDays"/> days.</summary>
public sealed record RecentAdherence(decimal AskedSets, decimal DoneSets);

/// <summary>
/// The rules behind the recommendations (docs/recommendations.md). All thresholds are product heuristics, not evidence: they
/// point at something to review, they do not diagnose anything.
/// </summary>
public static class Recommendations
{
    public const string RecurringPain = "recurring_pain";
    public const string Stalled = "stalled_exercise";
    public const string LowAdherence = "low_adherence";
    public const string Swap = "swap";
    public const string AdjustPlan = "adjust_plan";

    public const int PainWindow = 4;
    public const int PainExposures = 2;
    public const int RecentExposures = 3;
    public const int MinimumExposures = 6;
    public const int MinimumStallDays = 21;
    public const int AdherenceDays = 14;
    public const decimal AdherenceThreshold = 0.6m;
    public const decimal MinimumAskedSets = 20m;
    public const int MaximumStalled = 3;

    public const string PainTitle = "Molestias repetidas en este ejercicio";
    public const string PainWhy = "Has anotado dolor en {0} de las últimas {1} veces.";
    public const string PainSuggestion = "Cámbialo por una alternativa y, si el dolor sigue, consulta a un profesional sanitario.";
    public const string StalledTitle = "Sin avances en este ejercicio";
    public const string StalledWhy = "En las últimas {0} veces no has superado tu mejor marca estimada anterior.";
    public const string StalledSuggestion = "Revisa el descanso y el esfuerzo, o prueba una alternativa: también puede ser el sueño, la técnica o la variación normal.";
    public const string AdherenceTitle = "Estás haciendo menos de lo previsto";
    public const string AdherenceWhy = "En los últimos {1} días has completado el {0} % de las series previstas.";
    public const string AdherenceSuggestion = "Si te falta tiempo, acorta las sesiones o entrena menos días.";

    /// <param name="translate">Turns a source text (a template with its placeholders filled) into the reader's language.</param>
    /// <param name="sessionWith">The next pending session that has the exercise, if any: only then can it be swapped.</param>
    public static IReadOnlyList<RecommendationView> Build(
        IReadOnlyList<ExerciseExposure> exposures, RecentAdherence? adherence, Func<string, string> translate,
        Func<string, string> nameOf, Func<string, Guid?> sessionWith)
    {
        var result = new List<RecommendationView>();
        var stalled = new List<RecommendationView>();
        foreach (var group in exposures.GroupBy(e => e.ExerciseId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(e => e.At).ToList();
            // Only what the plan still asks for is worth a recommendation.
            if (sessionWith(group.Key) is not { } session) continue;
            var latest = ordered.TakeLast(PainWindow).ToList();
            var painful = latest.Count(e => e.Pain);
            if (painful >= PainExposures)
            {
                result.Add(new(RecurringPain, translate(PainTitle), translate(Fill(PainWhy, painful, latest.Count)), translate(PainSuggestion),
                    "medium", Swap, group.Key, nameOf(group.Key), session,
                    new Dictionary<string, decimal> { ["painful"] = painful, ["exposures"] = latest.Count }));
                continue;
            }
            var measured = ordered.Where(e => e.Training && e.EstimatedOneRepMaxKg is > 0).ToList();
            if (measured.Count < MinimumExposures || (measured[^1].At - measured[0].At).TotalDays < MinimumStallDays) continue;
            var recent = measured.TakeLast(RecentExposures).Max(e => e.EstimatedOneRepMaxKg!.Value);
            var before = measured.SkipLast(RecentExposures).Max(e => e.EstimatedOneRepMaxKg!.Value);
            if (recent > before) continue;
            stalled.Add(new(Stalled, translate(StalledTitle), translate(Fill(StalledWhy, RecentExposures)), translate(StalledSuggestion),
                "low", Swap, group.Key, nameOf(group.Key), session,
                new Dictionary<string, decimal> { ["recentBestKg"] = recent, ["previousBestKg"] = before }));
        }
        result.AddRange(stalled.OrderBy(s => s.Data!["recentBestKg"] / s.Data["previousBestKg"]).Take(MaximumStalled));

        if (adherence is { AskedSets: >= MinimumAskedSets } && adherence.DoneSets < adherence.AskedSets * AdherenceThreshold)
        {
            var percent = Math.Round(adherence.DoneSets / adherence.AskedSets * 100);
            result.Add(new(LowAdherence, translate(AdherenceTitle), translate(Fill(AdherenceWhy, percent, AdherenceDays)),
                translate(AdherenceSuggestion), "medium", AdjustPlan,
                Data: new Dictionary<string, decimal> { ["askedSets"] = adherence.AskedSets, ["doneSets"] = adherence.DoneSets }));
        }
        return result;
    }

    private static string Fill(string template, params object[] values) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, template, values);
}
