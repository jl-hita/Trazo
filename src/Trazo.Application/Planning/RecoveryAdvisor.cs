using Trazo.Application.Localization;
using Trazo.Training;

namespace Trazo.Application.Planning;

public enum RecoveryState
{
    /// <summary>The muscles of the session are recovered.</summary>
    Ready,
    /// <summary>Close to recovered (a few hours): it can be done, and it is better to wait a little.</summary>
    Caution,
    /// <summary>Muscles of the session are still recovering: another session or more rest is advised.</summary>
    Wait
}

/// <summary>What the user did to one exercise in a past workout, reduced to what recovery needs.</summary>
public sealed record RecoveryExercise(
    IReadOnlyList<string> PrimaryMuscles, IReadOnlyList<string> SecondaryMuscles, int CompletedSets, decimal? AverageRir, bool LoadedBefore, string ExerciseId = "");

/// <summary>A past workout: when it happened, what was done and how the user reported to feel.</summary>
public sealed record RecoveryExposure(
    DateTimeOffset PerformedAt, IReadOnlyList<RecoveryExercise> Exercises, string Recovery, decimal? SessionRpe, bool Pain);

public sealed record RecoveryPlannedExercise(IReadOnlyList<string> PrimaryMuscles, IReadOnlyList<string> SecondaryMuscles, int Sets, string ExerciseId = "");

/// <summary>A pending session the advisor can assess, with its prescribed work.</summary>
public sealed record RecoveryCandidate(Guid SessionId, int SequenceIndex, string Title, IReadOnlyList<RecoveryPlannedExercise> Exercises);

public sealed record RecoveryAssessment(
    Guid SessionId, int SequenceIndex, string Title, RecoveryState State, DateTimeOffset? AvailableAt, IReadOnlyList<string> Reasons);

public sealed record RecoveryRecommendation(RecoveryAssessment Next, IReadOnlyList<RecoveryAssessment> Alternatives);

/// <summary>
/// Estimates when the muscles of a pending session are likely to be recovered. This is advice, never a gate: the user can always log the
/// session. Each muscle has its own estimate, based on effective sets from the exercise-specific <c>TrainingStimulus</c> audit:
/// primary muscles and meaningful secondary contributors count fully; incidental secondary labels do not count. Sets closer to
/// failure (RIR), prior fatigue, reported recovery, session RPE and pain modify the estimate, which is scaled by experience.
/// A pending session is limited by its slowest meaningfully trained muscle; a session with no meaningful overlap is Ready.
/// The time coefficients and feedback multipliers below are product heuristics, not a validated physiological model, and are kept
/// together so they can be tuned against real usage. This remains in the application layer rather than the shared generation engine
/// because it consumes user feedback and returns advisory availability, not a prescription.
/// </summary>
public static class RecoveryAdvisor
{
    public const double BaseHours = 24;
    public const double HoursPerEffectiveSet = 5.5;
    public const double MaximumHours = 120;
    /// <summary>A muscle that received less than this many effective sets did not really get a stimulus to recover from.</summary>
    public const double NegligibleSets = 2;
    /// <summary>A pending session only counts a muscle if it works it with at least this many effective sets.</summary>
    public const double MeaningfulSets = 3;
    public const double CautionHours = 12;
    /// <summary>How many pending sessions are offered when the next one needs more rest.</summary>
    public const int AlternativeCandidates = 3;

    public static RecoveryRecommendation Recommend(
        IReadOnlyList<RecoveryCandidate> candidates, IReadOnlyList<RecoveryExposure> history, DateTimeOffset now, TrainingExperience experience,
        AppLanguage language = AppLanguage.Es)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) throw new ArgumentException("At least one candidate is needed.", nameof(candidates));
        var readyAt = MuscleReadiness(history, experience);
        var assessed = candidates.OrderBy(c => c.SequenceIndex).Take(AlternativeCandidates + 1)
            .Select(c => Assess(c, readyAt, now, language)).ToList();
        var next = assessed[0];
        var alternatives = next.State == RecoveryState.Ready
            ? []
            : assessed.Skip(1).Where(a => a.State == RecoveryState.Ready).ToList();
        return new RecoveryRecommendation(next, alternatives);
    }

    public static RecoveryAssessment Assess(
        RecoveryCandidate candidate, IReadOnlyList<RecoveryExposure> history, DateTimeOffset now, TrainingExperience experience,
        AppLanguage language = AppLanguage.Es) =>
        Assess(candidate, MuscleReadiness(history, experience), now, language);

    private sealed record Readiness(DateTimeOffset At, DateTimeOffset PerformedAt, double Sets);

    private static RecoveryAssessment Assess(RecoveryCandidate candidate, Dictionary<string, Readiness> readyAt, DateTimeOffset now, AppLanguage language)
    {
        var limiting = new List<(string Muscle, Readiness Readiness)>();
        foreach (var (muscle, sets) in EffectiveSets(candidate.Exercises.Select(e => (e.ExerciseId, e.PrimaryMuscles, e.SecondaryMuscles, (double)e.Sets))))
            if (sets >= MeaningfulSets && readyAt.TryGetValue(muscle, out var readiness) && readiness.At > now)
                limiting.Add((muscle, readiness));

        if (limiting.Count == 0)
            return new(candidate.SessionId, candidate.SequenceIndex, candidate.Title, RecoveryState.Ready, null, []);

        var availableAt = limiting.Max(l => l.Readiness.At);
        var state = availableAt - now <= TimeSpan.FromHours(CautionHours) ? RecoveryState.Caution : RecoveryState.Wait;
        var english = language == AppLanguage.En;
        var reasons = limiting.OrderByDescending(l => l.Readiness.At).Take(3).Select(l => english
            ? $"{Label(l.Muscle, language)}: worked {Hours(now - l.Readiness.PerformedAt, language)} ago ({Math.Round(l.Readiness.Sets)} effective sets); " +
              $"needs about {Hours(l.Readiness.At - now, language)} more."
            : $"{Label(l.Muscle, language)}: trabajado hace {Hours(now - l.Readiness.PerformedAt, language)} ({Math.Round(l.Readiness.Sets)} series efectivas); " +
              $"necesita unas {Hours(l.Readiness.At - now, language)} más.").ToList();
        return new(candidate.SessionId, candidate.SequenceIndex, candidate.Title, state, availableAt, reasons);
    }

    /// <summary>For each muscle, when it will be recovered according to the workout that keeps it busy the longest.</summary>
    private static Dictionary<string, Readiness> MuscleReadiness(IReadOnlyList<RecoveryExposure> history, TrainingExperience experience)
    {
        var result = new Dictionary<string, Readiness>(StringComparer.Ordinal);
        var experienceFactor = experience switch
        {
            TrainingExperience.Beginner => 0.85,
            TrainingExperience.Advanced => 1.1,
            _ => 1.0
        };
        foreach (var exposure in history)
        {
            var signals = SessionFactor(exposure);
            var weighted = new Dictionary<string, double>(StringComparer.Ordinal);
            var effective = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var exercise in exposure.Exercises.Where(e => e.CompletedSets > 0))
            {
                var factor = IntensityFactor(exercise.AverageRir) * (exercise.LoadedBefore ? 1.1 : 1.0);
                foreach (var (muscle, sets) in EffectiveSets([(exercise.ExerciseId, exercise.PrimaryMuscles, exercise.SecondaryMuscles, (double)exercise.CompletedSets)]))
                {
                    effective[muscle] = effective.GetValueOrDefault(muscle) + sets;
                    weighted[muscle] = weighted.GetValueOrDefault(muscle) + sets * factor;
                }
            }
            foreach (var (muscle, sets) in effective.Where(p => p.Value >= NegligibleSets))
            {
                var hours = Math.Min(MaximumHours, (BaseHours + HoursPerEffectiveSet * weighted[muscle]) * experienceFactor * signals);
                var at = exposure.PerformedAt.AddHours(hours);
                if (!result.TryGetValue(muscle, out var current) || at > current.At)
                    result[muscle] = new Readiness(at, exposure.PerformedAt, sets);
            }
        }
        return result;
    }

    /// <summary>
    /// Effective sets per muscle with the engine's own definition (<c>TrainingStimulus</c>): a set counts fully for the primary muscles and for the
    /// secondary ones with a meaningful contribution (the triceps in a press) and not at all for the incidental ones. The same definition the
    /// generator plans with and the load chart measures, so the volume, the dose and the recovery never disagree about one workout.
    /// </summary>
    private static Dictionary<string, double> EffectiveSets(IEnumerable<(string ExerciseId, IReadOnlyList<string> Primary, IReadOnlyList<string> Secondary, double Sets)> work)
    {
        var total = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (exerciseId, primary, secondary, sets) in work)
            foreach (var muscle in Trazo.Training.TrainingStimulus.EffectiveMuscles(exerciseId, primary, secondary))
                total[muscle] = total.GetValueOrDefault(muscle) + sets;
        return total;
    }

    /// <summary>Sets close to failure cost more to recover from than sets with plenty in reserve.</summary>
    private static double IntensityFactor(decimal? averageRir) => averageRir switch
    {
        null => 1.0,
        <= 1m => 1.2,
        >= 3m => 0.85,
        _ => 1.0
    };

    private static double SessionFactor(RecoveryExposure exposure)
    {
        var factor = exposure.Recovery switch
        {
            "Insufficient" => 1.25,
            "Partial" => 1.1,
            "Good" => 0.9,
            _ => 1.0
        };
        if (exposure.SessionRpe is >= 9m) factor *= 1.15;
        if (exposure.Pain) factor *= 1.25;
        return factor;
    }

    private static string Hours(TimeSpan span, AppLanguage language)
    {
        var hours = Math.Max(1, (int)Math.Round(span.TotalHours));
        return hours >= 48 ? $"{Math.Round(hours / 24.0, 1):0.#} {(language == AppLanguage.En ? "days" : "días")}" : $"{hours} h";
    }

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["Chest"] = "Pecho", ["Lats"] = "Dorsales", ["UpperBack"] = "Espalda alta", ["Deltoids"] = "Hombros", ["Biceps"] = "Bíceps",
        ["Triceps"] = "Tríceps", ["Forearms"] = "Antebrazos", ["Abdominals"] = "Abdomen", ["Obliques"] = "Oblicuos",
        ["ErectorSpinae"] = "Lumbares", ["Glutes"] = "Glúteos", ["Quadriceps"] = "Cuádriceps", ["Hamstrings"] = "Isquiotibiales",
        ["Calves"] = "Gemelos", ["Adductors"] = "Aductores", ["Abductors"] = "Abductores", ["HipFlexors"] = "Flexores de cadera",
        ["Serratus"] = "Serrato", ["TibialisAnterior"] = "Tibial anterior", ["RotatorCuff"] = "Manguito rotador"
    };

    private static readonly Dictionary<string, string> LabelsEn = new(StringComparer.Ordinal)
    {
        ["Chest"] = "Chest", ["Lats"] = "Lats", ["UpperBack"] = "Upper back", ["Deltoids"] = "Shoulders", ["Biceps"] = "Biceps",
        ["Triceps"] = "Triceps", ["Forearms"] = "Forearms", ["Abdominals"] = "Abs", ["Obliques"] = "Obliques",
        ["ErectorSpinae"] = "Lower back", ["Glutes"] = "Glutes", ["Quadriceps"] = "Quads", ["Hamstrings"] = "Hamstrings",
        ["Calves"] = "Calves", ["Adductors"] = "Adductors", ["Abductors"] = "Abductors", ["HipFlexors"] = "Hip flexors",
        ["Serratus"] = "Serratus", ["TibialisAnterior"] = "Tibialis anterior", ["RotatorCuff"] = "Rotator cuff"
    };

    private static string Label(string muscle, AppLanguage language) =>
        (language == AppLanguage.En ? LabelsEn : Labels).GetValueOrDefault(muscle, muscle);
}
