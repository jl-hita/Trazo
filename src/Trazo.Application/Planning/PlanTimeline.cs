namespace Trazo.Application.Planning;

/// <summary>A pending session as the timeline sees it: its place in the program, the period it belongs to and its planned date, if any.</summary>
public sealed record TimelineSession(Guid Id, int SequenceIndex, int Period, DateOnly? Date, string Title);

public sealed record TimelineNext(
    TimelineSession? Next, IReadOnlyList<TimelineSession> Candidates, IReadOnlyList<TimelineSession> Overdue);

/// <summary>
/// How a plan advances. The engine (generation, selection, prescription, progression, adaptation) is the same for every plan;
/// what changes between scheduling modes is only the answer to "which session comes next" and "what is late". Today there is
/// one implementation, <see cref="CalendarTimeline"/>; the sequence-driven (asynchronous) mode plugs in here.
/// </summary>
public interface IPlanTimeline
{
    string Mode { get; }

    /// <summary>The next session to do, the following ones that could be offered instead, and the ones that are late.</summary>
    /// <param name="pending">Sessions not yet resolved, in any order.</param>
    TimelineNext Next(IReadOnlyCollection<TimelineSession> pending, DateOnly today, int candidates);
}

/// <summary>
/// The sequence-driven (asynchronous) plan: there are no days, so the next session is the first pending one of the rotation and
/// nothing is ever "late". The user trains whenever it suits them; the program advances only when sessions are resolved.
/// </summary>
public sealed class SequenceTimeline : IPlanTimeline
{
    public string Mode => "Sequence";

    public TimelineNext Next(IReadOnlyCollection<TimelineSession> pending, DateOnly today, int candidates)
    {
        var ordered = pending.OrderBy(s => s.SequenceIndex).ToList();
        return new TimelineNext(ordered.FirstOrDefault(), ordered.Take(Math.Max(1, candidates)).ToList(), []);
    }
}

/// <summary>
/// The week-based plan: the next session is the first pending one of the program (by <see cref="TimelineSession.SequenceIndex"/>,
/// which follows the planned dates), even if its date has passed. A session is late when its planned date is before today.
/// </summary>
public sealed class CalendarTimeline : IPlanTimeline
{
    public string Mode => "Calendar";

    public TimelineNext Next(IReadOnlyCollection<TimelineSession> pending, DateOnly today, int candidates)
    {
        var ordered = pending.OrderBy(s => s.SequenceIndex).ToList();
        return new TimelineNext(
            ordered.FirstOrDefault(), ordered.Take(Math.Max(1, candidates)).ToList(),
            ordered.Where(s => s.Date is { } date && date < today).ToList());
    }
}
