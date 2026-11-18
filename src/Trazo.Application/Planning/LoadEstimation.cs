using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Estimates the load for the next session of an exercise from several past sessions instead of repeating the last one. Each session
/// contributes the best estimated maximum of its working sets (Epley with the reserve reported); the sessions are weighted 3:2:1 from the
/// latest, so a bad day does not drag the load down nor a good one push it up. The estimated maximum becomes the load that matches the
/// middle of the repetition range at the prescribed reserve, rounded to the usual increment and bounded around the last load. Pure.
/// </summary>
public static class LoadEstimation
{
    public const int MinimumSessions = 2;
    /// <summary>Largest change against the last load: down 10 %, up 5 % (the same cap as the progression).</summary>
    public const decimal MaximumDropPercent = 10m;
    public const decimal MaximumRisePercent = 5m;

    private static readonly decimal[] Weights = [3m, 2m, 1m];

    /// <param name="sessions">Working sets of each past session, the latest first.</param>
    /// <returns>The load to propose, or null when the evidence is not enough (fewer than two sessions with usable sets).</returns>
    public static decimal? Estimate(
        IReadOnlyList<IReadOnlyList<TrainingExecutedSet>> sessions, int minRepetitions, int maxRepetitions, decimal? targetRir, decimal lastLoadKg)
    {
        if (lastLoadKg <= 0 || minRepetitions < 1 || maxRepetitions < minRepetitions) return null;
        decimal sum = 0, weight = 0;
        var used = 0;
        for (var i = 0; i < Math.Min(sessions.Count, Weights.Length); i++)
        {
            var best = sessions[i].Where(s => s.Completed && s.WeightKg is > 0 && s.Repetitions is >= 1 and <= 30)
                .Select(s => TrainingLoadEstimator.EstimateOneRepMax(s.WeightKg!.Value, s.Repetitions!.Value, s.Rir))
                .DefaultIfEmpty(0m).Max();
            if (best <= 0) continue;
            sum += best * Weights[i];
            weight += Weights[i];
            used++;
        }
        if (used < MinimumSessions) return null;

        var oneRepMax = sum / weight;
        var reps = (minRepetitions + maxRepetitions) / 2;
        var raw = TrainingLoadEstimator.LoadForRepetitions(oneRepMax, reps, targetRir ?? 2m);
        var bounded = Math.Clamp(raw, lastLoadKg * (1 - MaximumDropPercent / 100m), lastLoadKg * (1 + MaximumRisePercent / 100m));
        return TrainingLoadEstimator.Round(bounded, lastLoadKg < 20m ? 1m : 2.5m);
    }
}
