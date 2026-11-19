namespace Trazo.Application.Planning.Frequency;

/// <summary>
/// Limits of the frequency reorganisation. Like <c>LongitudinalPolicy</c> they are PRODUCT criteria: conservative, easy to explain, not
/// physiological values and not measured. Changing one means bumping <see cref="Version"/>; every proposal records the version it used.
///
/// A reorganisation is only ever PROPOSED, never imposed. It never rewrites a block that already exists: once accepted it applies to the
/// blocks generated afterwards, and every record (proposal, workouts, decisions) is kept so it can be reverted without losing history.
/// </summary>
public static class FrequencyPolicy
{
    public const string Version = "frequency-1.0";

    /// <summary>Most recent complete calendar weeks looked at.</summary>
    public const int WindowWeeks = 6;
    /// <summary>Weeks with training (not absent, not deload) needed before anything is said about the pace.</summary>
    public const int MinimumEligibleWeeks = 4;
    /// <summary>Of the latest eligible weeks, how many are looked at together.</summary>
    public const int RecentWeeks = 4;
    /// <summary>Weeks below the planned days, out of <see cref="RecentWeeks"/>, for the pattern to count as persistent (one or two are an irregular week).</summary>
    public const int LowWeeksForPersistence = 3;
    /// <summary>The latest this many eligible weeks must all be low: a pattern that already ended is not a current availability.</summary>
    public const int LatestWeeksMustBeLow = 2;
    /// <summary>The low weeks may differ by this many days at most (3,3,2 is a pace; 3,1,2 is not).</summary>
    public const int MaximumSpreadOfLowWeeks = 1;
    /// <summary>Weeks without any workout in the window that make the pace unreadable (an absence is not a new availability).</summary>
    public const int MaximumAbsentWeeks = 1;
    /// <summary>Consecutive eligible weeks above the current days before offering to go back to the original frequency.</summary>
    public const int ReturnWeeks = 3;
    /// <summary>Fewest days a proposal will ever suggest.</summary>
    public const int MinimumDaysPerWeek = 2;
    /// <summary>A dismissed proposal is not offered again for the same days within this many days.</summary>
    public const int DismissCooldownDays = 28;
}
