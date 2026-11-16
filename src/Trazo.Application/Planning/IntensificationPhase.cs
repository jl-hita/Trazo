using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// What an intensification block changes in a prescription the engine generated as accumulation: the heavy lifts (those worked in
/// six repetitions or fewer) move to a lower repetition range, closer to failure and with more rest, and their load follows the
/// range; accessories keep their volume and range ("higher loads and low repetitions on the basics, keeping the accessory volume").
/// It lives here and not in the shared engine on purpose. Every figure is an internal product heuristic.
/// </summary>
public static class IntensificationPhase
{
    public const string PhaseType = "intensification";
    public const int HeavyLiftMaximumRepetitions = 6;
    private const int RepetitionFloor = 2;
    private const int RepetitionsLowered = 2;
    private const int ExtraRestSeconds = 30;
    private const int MaximumRestSeconds = 300;
    private const decimal MaximumLoadIncrease = 1.08m;

    public static IReadOnlyList<TrainingExercisePrescription> Apply(IReadOnlyList<TrainingExercisePrescription> exercises) =>
        exercises.Select(Apply).ToList();

    public static TrainingExercisePrescription Apply(TrainingExercisePrescription e)
    {
        if (e.MaxRepetitions > HeavyLiftMaximumRepetitions) return e;
        var min = Math.Max(RepetitionFloor, e.MinRepetitions - 1);
        var max = Math.Max(min + 2, e.MaxRepetitions - RepetitionsLowered);
        max = Math.Min(max, e.MaxRepetitions);
        min = Math.Min(min, max);
        var load = e.TargetLoadKg;
        var basis = e.LoadBasis;
        if (load is > 0 && max < e.MaxRepetitions)
        {
            // Epley: the same effort at fewer repetitions allows a heavier load.
            var factor = Math.Min(MaximumLoadIncrease, (1 + e.MaxRepetitions / 30m) / (1 + max / 30m));
            var step = e.SmallestLoadIncrementKg is > 0 ? e.SmallestLoadIncrementKg.Value : 0.5m;
            load = Math.Round(load.Value * factor / step, MidpointRounding.AwayFromZero) * step;
            basis = (basis is null ? "" : basis + " ") + "Fase de intensificación: carga ajustada a menos repeticiones.";
        }
        return e with
        {
            MinRepetitions = min, MaxRepetitions = max,
            TargetRepetitionsInReserve = e.TargetRepetitionsInReserve is { } rir ? Math.Max(1m, rir - 1m) : null,
            RestSeconds = Math.Min(MaximumRestSeconds, e.RestSeconds + ExtraRestSeconds),
            TargetLoadKg = load, LoadBasis = basis
        };
    }
}
