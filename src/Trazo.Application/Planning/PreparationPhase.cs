using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>How ready for training the user says they are when the plan is created.</summary>
public enum TrainingReadiness
{
    /// <summary>Trains regularly: no preparation.</summary>
    Regular,
    /// <summary>New to the gym.</summary>
    New,
    /// <summary>Comes back after a break.</summary>
    Returning
}

/// <summary>
/// The first weeks of a plan for somebody who is new or comes back after a break: a few weeks of little volume, far from failure,
/// before the plan proper (accumulation, intensification, deloads) starts. The engine generates the block as accumulation and this
/// lowers it, the same way <see cref="IntensificationPhase"/> raises the intensity of its block; the shared engine is untouched.
///
/// Every figure here is a product decision, NOT a figure prescribed by a guideline. What the literature supports is the direction:
/// beginners and people coming back gain strength with little volume (one to a few sets, moderate loads), whole-body training two or
/// three days a week, and a gentle start. It gives no exact number of weeks, percentage of volume or rest between sessions.
/// </summary>
public static class PreparationPhase
{
    public const string PhaseType = "preparation";
    public const int MinimumWeeks = 1;
    public const int MaximumWeeks = 4;
    public const int NewWeeks = 2;
    public const int ReturningShortBreakWeeks = 2;
    public const int ReturningLongBreakWeeks = 3;
    /// <summary>The break, in months, from which the suggestion for somebody coming back goes from two weeks to three.</summary>
    public const int LongBreakMonths = 3;
    /// <summary>Weeks of the plan proper that must remain after the preparation.</summary>
    public const int MinimumWeeksAfter = PlanProfileParser.MinimumTotalWeeks;

    private const decimal FirstWeekVolume = 0.5m;
    private const decimal LastWeekVolume = 0.75m;
    private const decimal FirstWeeksRir = 4m;
    private const decimal LastWeekRir = 3m;

    /// <summary>Weeks suggested when the client sends none: new 2, coming back 2 (3 for a long break, which the client tells by sending the weeks).</summary>
    public static int DefaultWeeks(TrainingReadiness readiness) => readiness switch
    {
        TrainingReadiness.New => NewWeeks,
        TrainingReadiness.Returning => ReturningShortBreakWeeks,
        _ => 0
    };

    /// <summary>Fraction of the generated sets that week <paramref name="week"/> (1-based) of a preparation of <paramref name="weeks"/> keeps.</summary>
    public static decimal VolumeFactor(int week, int weeks) =>
        weeks <= 1 ? FirstWeekVolume : FirstWeekVolume + (LastWeekVolume - FirstWeekVolume) * (Math.Clamp(week, 1, weeks) - 1) / (weeks - 1);

    /// <summary>Repetitions in reserve asked of week <paramref name="week"/>: far from failure, a little closer in the last week.</summary>
    public static decimal RepetitionsInReserve(int week, int weeks) => weeks > 1 && week >= weeks ? LastWeekRir : FirstWeeksRir;

    public static IReadOnlyList<TrainingExercisePrescription> Apply(IReadOnlyList<TrainingExercisePrescription> exercises, int week, int weeks)
    {
        var factor = VolumeFactor(week, weeks);
        var rir = RepetitionsInReserve(week, weeks);
        return exercises.Select(e => e with
        {
            Sets = Math.Max(1, (int)Math.Round(e.Sets * factor, MidpointRounding.AwayFromZero)),
            TargetRepetitionsInReserve = Math.Max(e.TargetRepetitionsInReserve ?? 0m, rir)
        }).ToList();
    }

    public static string Objective =>
        "Llegar al plan con la técnica aprendida y el cuerpo adaptado: pocas series, lejos del fallo y con tiempo para recuperar entre sesiones.";
}
