using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>A block of consecutive weeks of one phase that is generated as a unit.</summary>
public sealed record MesocycleSlot(int Sequence, int PhaseSequenceNumber, string PhaseType, int StartWeek, int Weeks)
{
    public bool IsDeload => PhaseType == "deload";
    public int EndWeek => StartWeek + Weeks - 1;
    public bool Contains(int week) => week >= StartWeek && week <= EndWeek;
}

public sealed record PhaseSpan(int Sequence, string Type, DateOnly Start, DateOnly End);

public enum PlanTimeline
{
    NotStarted,
    InProgress,
    Finished
}

public sealed record PlanPosition(PlanTimeline State, int Week);

/// <summary>Pure calendar rules for plans: how a macrocycle is cut into blocks and which week a date belongs to.</summary>
public static class PlanSchedule
{
    /// <summary>Longest block generated at once. Short blocks let the next one use what was really done.</summary>
    public const int MaxMesocycleWeeks = 4;

    public static IReadOnlyList<MesocycleSlot> Slots(TrainingMacrocycleDraft macrocycle)
    {
        ArgumentNullException.ThrowIfNull(macrocycle);
        return Slots(macrocycle.StartDate,
            macrocycle.Phases.Select(p => new PhaseSpan(p.SequenceNumber, p.PhaseType, p.StartDate, p.EndDate)));
    }

    /// <summary>Cuts every phase of the macrocycle into blocks of at most <see cref="MaxMesocycleWeeks"/> weeks.</summary>
    public static IReadOnlyList<MesocycleSlot> Slots(DateOnly planStart, IEnumerable<PhaseSpan> phases)
    {
        var slots = new List<MesocycleSlot>();
        foreach (var phase in phases.OrderBy(p => p.Sequence))
        {
            var weeks = (phase.End.DayNumber - phase.Start.DayNumber + 1) / 7;
            var firstWeek = (phase.Start.DayNumber - planStart.DayNumber) / 7 + 1;
            for (var offset = 0; offset < weeks; offset += MaxMesocycleWeeks)
                slots.Add(new MesocycleSlot(
                    slots.Count + 1, phase.Sequence, phase.Type, firstWeek + offset,
                    Math.Min(MaxMesocycleWeeks, weeks - offset)));
        }
        return slots;
    }

    /// <summary>Date of a training day inside a plan week: the week starts on the plan's start date and lasts seven days.</summary>
    public static DateOnly DateFor(DateOnly planStart, int week, DayOfWeek day)
    {
        var weekStart = planStart.AddDays(7 * (week - 1));
        var offset = ((int)day - (int)weekStart.DayOfWeek + 7) % 7;
        return weekStart.AddDays(offset);
    }

    public static DateOnly WeekStart(DateOnly planStart, int week) => planStart.AddDays(7 * (week - 1));

    public static PlanPosition Position(DateOnly planStart, int totalWeeks, DateOnly today)
    {
        if (today < planStart) return new(PlanTimeline.NotStarted, 1);
        var week = (today.DayNumber - planStart.DayNumber) / 7 + 1;
        return week > totalWeeks ? new(PlanTimeline.Finished, totalWeeks) : new(PlanTimeline.InProgress, week);
    }
}
