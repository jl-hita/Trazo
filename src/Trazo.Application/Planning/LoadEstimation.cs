using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Estimates a target load for the next exposure of the same exercise, using up to its three latest sessions (latest first) rather than
/// blindly repeating the last load. For each usable session it takes the highest estimated 1RM among completed working sets (Epley,
/// accounting for reported RIR), then combines those estimates with recency weights 3:2:1. At least two sessions must contain usable
/// sets; otherwise there is not enough evidence and the caller keeps the ordinary repeat/increase progression. The combined estimate
/// is converted to the midpoint of the prescribed rep range at the target RIR, rounded to the equipment increment, and bounded to
/// -10%/+5% of the last load. It is exercise-specific: history from a similar movement is not substituted. Pure.
/// </summary>
public static class LoadEstimation
{
    public const int MinimumSessions = 2;
    /// <summary>Largest change against the last load: down 10 %, up 5 % (the same cap as the progression).</summary>
    public const decimal MaximumDropPercent = 10m;
    public const decimal MaximumRisePercent = 5m;

    private static readonly decimal[] Weights = [3m, 2m, 1m];

    /// <param name="sessions">Working sets grouped by session, newest first; only the first three sessions are considered.</param>
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
