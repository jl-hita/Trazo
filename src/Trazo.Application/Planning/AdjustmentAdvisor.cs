namespace Trazo.Application.Planning;

/// <summary>What the plan should do about the sessions the user has not done. See "Sessions not done" in docs/planning-engine.md.</summary>
public enum AdjustmentStrategy
{
    /// <summary>Nothing to adjust: the next session is simply the next one (a late session stays pending, which is the queue shift).</summary>
    Continue,
    /// <summary>Calendar plan behind by whole weeks: move the pending program to the current week.</summary>
    Shift,
    /// <summary>A long absence: the first sessions back are lighter.</summary>
    EaseBack,
    /// <summary>The plan no longer fits the person: build a new one with their current profile.</summary>
    Rebuild
}

/// <summary>Why the advisor chose its strategy. Clients translate by code; the API also sends a ready message.</summary>
public enum AdjustmentReason
{
    OnTrack,
    LateSessions,
    BehindCalendar,
    AbsenceShort,
    AbsenceLong,
    AbsenceVeryLong,
    RepeatedSkips
}

/// <summary>The state of the training the advisor reads. Everything is derived from persisted facts; nothing is stored.</summary>
/// <param name="Sequence">A sequence plan: no dates, nothing is ever late.</param>
/// <param name="DaysSinceTraining">Days since the newest workout that was really done (an omitted session is not training); null when none yet.</param>
/// <param name="OverdueSessions">Pending sessions whose planned date has passed (calendar only).</param>
/// <param name="WeeksBehind">Whole weeks the pending program is behind the calendar (calendar only).</param>
/// <param name="RecentResolved">How many of the last resolved sessions are considered (at most <see cref="AdjustmentAdvisor.SkipWindow"/>).</param>
/// <param name="RecentSkipped">How many of those were explicitly omitted (Skipped); incomplete workouts are not omissions.</param>
public sealed record AdjustmentInput(bool Sequence, int? DaysSinceTraining, int OverdueSessions, int WeeksBehind, int RecentResolved, int RecentSkipped);

/// <param name="EaseBackSessions">How many of the next sessions are made lighter after the absence (0, 1 or 2), whatever the strategy.</param>
/// <param name="SuggestSkip">The oldest late session is worth considering for omission (calendar only); the user must decide.</param>
/// <param name="CanReschedule">A calendar plan is at least one whole week behind and can move its pending dates forward.</param>
public sealed record AdjustmentAdvice(AdjustmentStrategy Strategy, AdjustmentReason Reason, int EaseBackSessions, bool CanReschedule, bool SuggestSkip);

/// <summary>
/// Chooses what to do about sessions not done from the real state of the training, so a one-off absence never turns into a volume
/// change. Strategies are evaluated in this precedence: rebuild after a very long absence or repeated omissions; otherwise ease back
/// after a qualifying absence; otherwise offer a calendar shift when the plan is at least a week behind; otherwise continue the queue.
/// A late session remains pending and in order. Omitting is always the user's decision; this advisor only suggests it. Pure and deterministic.
/// </summary>
public static class AdjustmentAdvisor
{
    /// <summary>Days without training from which the first session back is lighter.</summary>
    public const int EaseBackAfterDays = 21;
    /// <summary>Days without training from which the first two sessions back are lighter.</summary>
    public const int DeepEaseBackAfterDays = 42;
    /// <summary>Days without training from which the plan is probably not the person's any more.</summary>
    public const int RebuildAfterDays = 84;
    /// <summary>How many of the last resolved sessions are looked at to detect a plan that does not fit.</summary>
    public const int SkipWindow = 5;
    /// <summary>Omitted sessions within <see cref="SkipWindow"/> that suggest rebuilding.</summary>
    public const int RebuildAfterSkips = 3;
    /// <summary>Late sessions of a calendar plan from which omitting the oldest is suggested (the queue is too long to recover).</summary>
    public const int SuggestSkipAtOverdue = 2;

    /// <summary>How many of the next sessions are lighter after <paramref name="daysSinceTraining"/> days away (independent of the strategy chosen).</summary>
    public static int EaseBackSessionsFor(int? daysSinceTraining) =>
        daysSinceTraining >= DeepEaseBackAfterDays ? 2 : daysSinceTraining >= EaseBackAfterDays ? 1 : 0;

    /// <summary>
    /// Applies the strategy precedence documented on this type. Rebuild and ease-back are driven by the real absence/omission history;
    /// shifting is only available to calendar plans; sequence plans simply continue their session order.
    /// </summary>
    public static AdjustmentAdvice Advise(AdjustmentInput input)
    {
        var days = input.DaysSinceTraining;
        var canReschedule = !input.Sequence && input.WeeksBehind >= 1;
        var ease = EaseBackSessionsFor(days);

        if (days >= RebuildAfterDays)
            return new(AdjustmentStrategy.Rebuild, AdjustmentReason.AbsenceVeryLong, ease, canReschedule, false);
        if (input.RecentResolved >= RebuildAfterSkips && input.RecentSkipped >= RebuildAfterSkips)
            return new(AdjustmentStrategy.Rebuild, AdjustmentReason.RepeatedSkips, ease, canReschedule, false);

        if (ease > 0)
            return new(AdjustmentStrategy.EaseBack, ease == 2 ? AdjustmentReason.AbsenceLong : AdjustmentReason.AbsenceShort, ease, canReschedule, false);

        if (canReschedule)
            return new(AdjustmentStrategy.Shift, AdjustmentReason.BehindCalendar, 0, true, false);
        if (!input.Sequence && input.OverdueSessions > 0)
            return new(AdjustmentStrategy.Continue, AdjustmentReason.LateSessions, 0, false, input.OverdueSessions >= SuggestSkipAtOverdue);
        return new(AdjustmentStrategy.Continue, AdjustmentReason.OnTrack, 0, false, false);
    }
}
