namespace Trazo.Application.Planning;

/// <summary>
/// The recovery of a workout as the adaptation reads it. Apps that still ask about it for the whole session send
/// <c>Unknown|Good|Partial|Insufficient</c> and that answer is used as it is. Apps that ask about each muscle instead send
/// nothing, and the same reading is worked out from those answers: for one exercise from the muscles it trains as primary, and
/// for the session from how much of its planned work falls on muscles that had not recovered. Thresholds are internal product
/// values. The stored log keeps what the person said: nothing derived is written over it.
/// </summary>
public static class SessionRecovery
{
    public const string Unknown = "Unknown";
    public const string Good = "Good";
    public const string Partial = "Partial";
    public const string Insufficient = "Insufficient";
    /// <summary>From this share of the planned sets on muscles not recovered, the whole session counts as not recovered.</summary>
    public const decimal InsufficientShare = 0.5m;

    private const string NotRecovered = "notRecovered";

    /// <summary>One exercise: not recovered if any muscle it trains as primary was not.</summary>
    public static string ForExercise(
        string reported, IReadOnlyDictionary<string, MuscleFeedbackInput> answers, IEnumerable<string> primaryMuscles)
    {
        if (Reported(reported)) return reported;
        var given = primaryMuscles.Select(m => answers.TryGetValue(m, out var a) ? a.Recovery : null).Where(r => r is not null).ToList();
        if (given.Count == 0) return Unknown;
        return given.Any(Is) ? Insufficient : Good;
    }

    /// <param name="work">The exercises of the session: the muscles each trains as primary and its planned sets.</param>
    public static string ForSession(
        string reported, IReadOnlyDictionary<string, MuscleFeedbackInput> answers,
        IEnumerable<(IReadOnlyCollection<string> PrimaryMuscles, int Sets)> work)
    {
        if (Reported(reported)) return reported;
        if (answers.Values.All(a => a.Recovery is null)) return Unknown;
        var total = 0;
        var tired = 0;
        foreach (var (muscles, sets) in work)
        {
            total += sets;
            if (muscles.Any(m => answers.TryGetValue(m, out var a) && Is(a.Recovery))) tired += sets;
        }
        if (tired == 0) return Good;
        return tired >= InsufficientShare * total ? Insufficient : Partial;
    }

    private static bool Reported(string value) =>
        !string.IsNullOrWhiteSpace(value) && !string.Equals(value, Unknown, StringComparison.OrdinalIgnoreCase);

    private static bool Is(string? recovery) => string.Equals(recovery, NotRecovered, StringComparison.OrdinalIgnoreCase);
}
