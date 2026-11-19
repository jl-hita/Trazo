namespace Trazo.Application.Planning;

public enum FrequencyState
{
    /// <summary>Not enough evidence of the user's pace (see <see cref="LoadMetrics"/>), or a plan with no calendar.</summary>
    Unknown,
    OnTrack,
    /// <summary>The user trains clearly less often than the plan assumes.</summary>
    Below,
    /// <summary>The user trains clearly more often than the plan assumes.</summary>
    Above
}

/// <param name="Observed">Sessions per seven days really done in the last weeks; null when <see cref="LoadMetrics"/> had too little evidence.</param>
/// <param name="Nominal">Sessions per seven days the plan was built for.</param>
/// <param name="SuggestedSessionsPerWeek">For <see cref="FrequencyState.Below"/>: the frequency a new plan could assume (the pace rounded, never under 2); otherwise null.</param>
public sealed record FrequencyAdvice(FrequencyState State, decimal? Observed, int Nominal, int? SuggestedSessionsPerWeek);

/// <summary>
/// Compares the pace the user really keeps with the pace the plan was built for. A plan assumes N sessions per week; a person who
/// trains fewer receives less weekly dose than the plan prescribes and one who trains more receives more, whatever the number of the
/// week says (see "Dose per seven days" in <see cref="LoadMetrics"/>). The advisor never rewrites the plan: it tells the truth about the
/// pace and, when it is clearly lower, which frequency a new plan could assume. Doing less often is not an error to correct with more sets in
/// each session. The thresholds are product conventions, not physiological values. Pure and deterministic.
/// </summary>
public static class FrequencyAdvisor
{
    /// <summary>The user must be this many sessions per week away from the plan before it is reported (a shy of one session is noise).</summary>
    public const decimal ToleranceSessionsPerWeek = 1m;
    public const int MinimumSuggestedSessionsPerWeek = 2;

    public static FrequencyAdvice Advise(bool sequence, int nominalSessionsPerWeek, decimal? observedSessionsPerSevenDays)
    {
        // A rotation has no calendar: its "week" is a group of sessions, so a pace in days says nothing about it.
        if (sequence || nominalSessionsPerWeek <= 0 || observedSessionsPerSevenDays is not { } observed)
            return new(FrequencyState.Unknown, sequence ? null : observedSessionsPerSevenDays, Math.Max(0, nominalSessionsPerWeek), null);

        if (observed <= nominalSessionsPerWeek - ToleranceSessionsPerWeek)
        {
            var suggested = (int)Math.Round(observed, MidpointRounding.AwayFromZero);
            suggested = Math.Min(nominalSessionsPerWeek - 1, Math.Max(MinimumSuggestedSessionsPerWeek, suggested));
            return new(FrequencyState.Below, observed, nominalSessionsPerWeek, suggested);
        }
        if (observed >= nominalSessionsPerWeek + ToleranceSessionsPerWeek)
            return new(FrequencyState.Above, observed, nominalSessionsPerWeek, null);
        return new(FrequencyState.OnTrack, observed, nominalSessionsPerWeek, null);
    }
}
