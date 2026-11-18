namespace Trazo.Training;

/// <summary>
/// A plan that advances by the order of its sessions instead of by calendar days (the "sequence" or asynchronous mode).
/// <para>
/// The generator produces the sessions of a block as a slice of one endless rotation: session number <c>p</c> of the plan trains the
/// template at position <c>p mod K</c> of the split's cycle (see <see cref="TrainingSessionRotation"/>), whatever the period it falls in.
/// A period (what clients call <c>weekNumber</c>) is only a group of <see cref="SessionsPerPeriod"/> consecutive sessions used to measure
/// progress and to place blocks and deloads; it is never a point where the rotation restarts.
/// </para>
/// </summary>
/// <param name="SessionsPerPeriod">Sessions in one period (2-6): the user's habitual number of sessions in seven days.</param>
/// <param name="StartPosition">Zero-based position in the rotation of the first session of the block: the number of sessions the plan
/// already contains. The caller derives it from what is persisted; the engine keeps no state.</param>
public sealed record TrainingSessionSequence(int SessionsPerPeriod, int StartPosition = 0);

/// <summary>A session type of a split: its title and the movement patterns it covers.</summary>
internal sealed record SessionTemplate(string Title, IReadOnlyList<TrainingMovementPattern> Patterns);

/// <summary>
/// The session templates of every split, in the two shapes the generator needs:
/// <list type="bullet">
/// <item><b>Weekly</b> (calendar plans): the list of sessions of one week for a number of training days. Only the combinations the
/// engine has always supported exist (see <see cref="IsSupportedWeekly"/>); nothing is relaxed for the sake of the sequence mode.</item>
/// <item><b>Cycle</b> (sequence plans): the closed list of K distinct sessions the plan walks through, over and over, in order.
/// Any number of sessions per period from 2 to 6 can be laid over it; where the period ends is irrelevant to the rotation.</item>
/// </list>
/// A group of sessions the engine plans together (a week, or one pass through the cycle) is a <i>unit</i>. For a calendar plan a unit is
/// exactly one week (K = N, one week); for a sequence plan it is one cycle, which spans K/N nominal weeks at N sessions per seven days.
/// </summary>
public static class TrainingSessionRotation
{
    public const int MinimumSessionsPerPeriod = 2;
    public const int MaximumSessionsPerPeriod = 6;

    private static readonly SessionTemplate FullBodyA = new("Cuerpo completo A",
        [TrainingMovementPattern.Squat, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.Hinge, TrainingMovementPattern.Core]);

    // B templates reverse the order of coexisting large groups (push/pull, quads/posterior chain)
    // so the muscle that was trained second in A is trained fresh first in B. The squat stays
    // first in full-body sessions; only the upper-body pair is swapped.
    private static readonly SessionTemplate FullBodyB = new("Cuerpo completo B",
        [TrainingMovementPattern.Squat, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate UpperA = new("Tren superior A",
        [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate LowerA = new("Tren inferior A",
        [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate UpperB = new("Tren superior B",
        [TrainingMovementPattern.VerticalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate LowerB = new("Tren inferior B",
        [TrainingMovementPattern.Hinge, TrainingMovementPattern.Squat, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate Push = new("Empuje",
        [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate Pull = new("Tracción",
        [TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate PushB = new("Empuje B", [TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.Core]);
    private static readonly SessionTemplate PullB = new("Tracción B", [TrainingMovementPattern.VerticalPull, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate Legs = new("Pierna",
        [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

    private static readonly SessionTemplate LegsB = Legs with { Title = "Pierna B", Patterns = LowerB.Patterns };

    /// <summary>Whether the calendar plan (a fixed set of days a week) supports this split for that number of days.</summary>
    public static bool IsSupportedWeekly(TrainingProgramSplit split, int daysPerWeek) => split switch
    {
        TrainingProgramSplit.Automatic => daysPerWeek is >= 2 and <= 4,
        TrainingProgramSplit.FullBody => daysPerWeek is >= 2 and <= 4,
        TrainingProgramSplit.UpperLower => daysPerWeek is 2 or 4,
        TrainingProgramSplit.PushPullLegs => daysPerWeek is 3 or 5 or 6,
        _ => false
    };

    /// <summary>
    /// Whether a sequence plan supports this split with that many sessions per period. The rotation is continuous, so a period
    /// does not need to hold a whole number of splits: full body and upper/lower take 2 to 6, push/pull/legs 3 to 6.
    /// </summary>
    public static bool IsSupportedSequence(TrainingProgramSplit split, int sessionsPerPeriod) =>
        sessionsPerPeriod is >= MinimumSessionsPerPeriod and <= MaximumSessionsPerPeriod && split switch
        {
            TrainingProgramSplit.Automatic or TrainingProgramSplit.FullBody or TrainingProgramSplit.UpperLower => true,
            TrainingProgramSplit.PushPullLegs => sessionsPerPeriod >= 3,
            _ => false
        };

    /// <summary>The week of a calendar plan: the sessions of the split for that number of days, or empty when unsupported.</summary>
    internal static IReadOnlyList<SessionTemplate> Weekly(TrainingProgramSplit split, int daysPerWeek)
    {
        var fullBodyC = FullBodyA with { Title = "Cuerpo completo C" };
        var fullBodyD = FullBodyB with { Title = "Cuerpo completo D" };
        return split switch
        {
            TrainingProgramSplit.FullBody when daysPerWeek == 2 => [FullBodyA, FullBodyB],
            TrainingProgramSplit.FullBody when daysPerWeek == 3 => [FullBodyA, FullBodyB, fullBodyC],
            TrainingProgramSplit.FullBody when daysPerWeek == 4 => [FullBodyA, FullBodyB, fullBodyC, fullBodyD],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 5 => [Push, Pull, Legs, PushB, PullB],
            TrainingProgramSplit.UpperLower when daysPerWeek == 2 => [UpperA, LowerA],
            TrainingProgramSplit.UpperLower when daysPerWeek == 4 => [UpperA, LowerA, UpperB, LowerB],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 3 => [Push, Pull, Legs],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 6 => [Push, Pull, Legs, PushB, PullB, LegsB],
            TrainingProgramSplit.Automatic => daysPerWeek switch
            {
                2 => [FullBodyA, FullBodyB],
                3 => [FullBodyA, FullBodyB, fullBodyC],
                4 => [UpperA, LowerA, UpperB, LowerB],
                _ => []
            },
            _ => []
        };
    }

    /// <summary>
    /// The closed cycle a sequence plan walks through. Full body alternates A and B; upper/lower alternates upper and lower with
    /// their A and B variants (four sessions); push/pull/legs runs push, pull, legs and their B variants (six sessions).
    /// "Automatic" picks the split by the sessions of a period: full body up to 3, upper/lower for 4 or 5, push/pull/legs for 6
    /// (an internal product choice: below four sessions a muscle is trained at least every other session only with full-body
    /// work, from six the legs and the push and pull days can each have their own sessions).
    /// </summary>
    internal static IReadOnlyList<SessionTemplate> Cycle(TrainingProgramSplit split, int sessionsPerPeriod)
    {
        if (split == TrainingProgramSplit.Automatic)
            split = sessionsPerPeriod switch
            {
                <= 3 => TrainingProgramSplit.FullBody,
                <= 5 => TrainingProgramSplit.UpperLower,
                _ => TrainingProgramSplit.PushPullLegs
            };
        return split switch
        {
            TrainingProgramSplit.FullBody => [FullBodyA, FullBodyB],
            TrainingProgramSplit.UpperLower => [UpperA, LowerA, UpperB, LowerB],
            TrainingProgramSplit.PushPullLegs => [Push, Pull, Legs, PushB, PullB, LegsB],
            _ => []
        };
    }

    /// <summary>
    /// Keys that tell the sessions of one group apart inside the engine (its allocation and ramps are keyed by week and day). A sequence
    /// plan has no days: these ascending weekdays are internal slot labels, never dates and never stored or shown.
    /// </summary>
    internal static IReadOnlyList<DayOfWeek> Slots(int sessions) => sessions switch
    {
        <= 2 => [DayOfWeek.Monday, DayOfWeek.Thursday],
        3 => [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        4 => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        5 => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday],
        _ => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]
    };
}

/// <summary>
/// The group of sessions the generator plans together and how it is laid out over the weeks of a block.
/// For a calendar plan the unit is one week (its templates are the weekly split, its days the training days) and it repeats every
/// week. For a sequence plan the unit is one pass through the cycle; the weeks of the block are consecutive slices of that cycle
/// that start where the previous slice (or the previous block) ended.
/// </summary>
internal sealed record GenerationUnit(
    IReadOnlyList<SessionTemplate> Templates,
    IReadOnlyList<DayOfWeek> Days,
    IReadOnlyList<DayOfWeek> PeriodDays,
    int SessionsPerPeriod,
    int StartPosition,
    decimal WeeksPerUnit,
    bool IsSequence)
{
    public static GenerationUnit For(TrainingProgramGenerationRequest request)
    {
        if (request.Sequence is { } sequence)
        {
            var cycle = TrainingSessionRotation.Cycle(request.Split, sequence.SessionsPerPeriod);
            return new(cycle, TrainingSessionRotation.Slots(cycle.Count), TrainingSessionRotation.Slots(sequence.SessionsPerPeriod),
                sequence.SessionsPerPeriod, sequence.StartPosition, cycle.Count / (decimal)sequence.SessionsPerPeriod, true);
        }

        var days = request.TrainingDays.Distinct().OrderBy(day => ((int)day + 6) % 7).ToArray();
        return new(TrainingSessionRotation.Weekly(request.Split, days.Length), days, days, days.Length, 0, 1m, false);
    }

    /// <summary>Lays the allocated sessions of the unit over <paramref name="weeks"/> periods of <see cref="SessionsPerPeriod"/> sessions.</summary>
    public IReadOnlyList<TrainingSessionPrescription> LayOut(IReadOnlyList<TrainingSessionPrescription> allocated, int weeks)
    {
        var byDay = allocated.ToDictionary(session => session.Day);
        var result = new List<TrainingSessionPrescription>(weeks * SessionsPerPeriod);
        for (var week = 1; week <= weeks; week++)
            for (var index = 0; index < SessionsPerPeriod; index++)
            {
                var position = StartPosition + (week - 1) * SessionsPerPeriod + index;
                result.Add(byDay[Days[position % Templates.Count]] with { WeekNumber = week, Day = PeriodDays[index] });
            }
        return result.OrderBy(session => session.WeekNumber).ThenBy(session => session.Day).ToArray();
    }

    /// <summary>
    /// The allocator reports its warnings for "week 1" of the unit. A calendar plan repeats that week, so each warning is repeated for
    /// every week of the block; a sequence plan has no such weeks, so its warnings say they are about each pass through the rotation.
    /// </summary>
    public IReadOnlyList<string> Warnings(IReadOnlyList<string> allocation, int weeks)
    {
        const string prefix = "Semana 1: ";
        var general = allocation.Where(warning => !warning.StartsWith(prefix, StringComparison.Ordinal));
        var perUnit = allocation.Where(warning => warning.StartsWith(prefix, StringComparison.Ordinal)).Select(warning => warning[prefix.Length..]).ToArray();
        if (IsSequence)
            return general.Concat(perUnit.Select(warning => $"En cada rotación de {Templates.Count} sesiones: {warning}")).ToArray();
        return general.Concat(Enumerable.Range(1, weeks).SelectMany(week => perUnit.Select(warning => $"Semana {week}: {warning}"))).ToArray();
    }
}
