namespace Trazo.Training;

/// <summary>
/// How much work a muscle gets in a plan, relative to the default dose of its level.
/// <c>Grow</c> is the default and is not stored. These are product choices, not physiological categories.
/// </summary>
public enum TrainingMusclePriority
{
    Grow,
    Emphasize,
    Maintain
}

public sealed record TrainingMusclePriorityResult(
    IReadOnlyList<TrainingMuscleVolumeTarget> Targets, IReadOnlyList<string> Errors, IReadOnlyList<string> Notes)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Turns the user's per-muscle priorities into the weekly volume targets the allocator already understands. It adds no new
/// volume model: it only chooses a target relative to the default dose of the plan's goal and level.
///
/// - <b>Grow</b>: no entry. The muscle keeps the default dose.
/// - <b>Emphasize</b>: the TOP of the agreed default range (default + half-width of the range), never above
///   <see cref="EmphasisTargetCeiling"/>. The allocator then allows the usual +/-2 sets around it, so the weekly ceiling of an
///   emphasized muscle stays at 20 effective sets. At most <see cref="MaximumEmphasizedMuscles"/> muscles can be emphasized:
///   the time of a session is finite and every emphasized muscle takes it from the others.
/// - <b>Maintain</b>: half of the default dose, never below <see cref="MaintenanceFloorSets"/>. Indirect work still counts:
///   a maintained triceps is not given isolation work if the presses already train it.
///
/// The numbers (half the dose, top of the range, 20 sets) are internal planning heuristics, not validated physiological
/// thresholds; they are constants here so they can be tuned in one place.
/// </summary>
public static class TrainingMusclePriorityPlanner
{
    public const int MaximumEmphasizedMuscles = 3;
    public const decimal EmphasisTargetCeiling = 18m;
    public const decimal MaintenanceFloorSets = 2m;

    /// <summary>
    /// Muscles a user can prioritise. Core and the small stabilisers are left out on purpose: direct core work is opt-in
    /// in the engine and is not a programming priority of the product.
    /// </summary>
    public static readonly IReadOnlyList<TrainingMuscleGroup> Configurable =
    [
        TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Deltoids,
        TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Hamstrings,
        TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Calves
    ];

    public static TrainingMusclePriorityResult Build(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyDictionary<TrainingMuscleGroup, TrainingMusclePriority>? priorities,
        int sessionsPerWeek,
        int maximumSessionMinutes)
    {
        var chosen = (priorities ?? new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>())
            .Where(pair => pair.Value != TrainingMusclePriority.Grow).OrderBy(pair => pair.Key).ToArray();
        if (chosen.Length == 0) return new([], [], []);

        var errors = new List<string>();
        if (goal != TrainingGoal.Hypertrophy)
            errors.Add("Las prioridades musculares solo se aplican a planes de hipertrofia.");
        foreach (var (muscle, priority) in chosen)
        {
            if (!Enum.IsDefined(muscle) || !Enum.IsDefined(priority) || !Configurable.Contains(muscle))
                errors.Add($"El grupo muscular {muscle} no admite prioridad.");
        }
        var emphasized = chosen.Count(pair => pair.Value == TrainingMusclePriority.Emphasize);
        if (emphasized > MaximumEmphasizedMuscles)
            errors.Add($"Se pueden enfatizar como máximo {MaximumEmphasizedMuscles} músculos a la vez: cada uno resta tiempo de sesión a los demás.");
        if (errors.Count > 0) return new([], errors, []);

        var defaults = TrainingProgramVolumeAllocator.GetWeeklyTargets(goal, experience, null, sessionsPerWeek, maximumSessionMinutes);
        // Muscles with no default target (calves in a short beginner plan, ...) are measured against half of the central dose.
        var central = defaults.TryGetValue(TrainingMuscleGroup.Chest, out var chest) ? chest : 10m;
        decimal DefaultOf(TrainingMuscleGroup muscle) =>
            defaults.TryGetValue(muscle, out var value) ? value : Math.Max(1m, Math.Ceiling(central / 2m));
        var tolerance = TrainingProgramVolumeAllocator.DefaultTargetTolerance(experience);

        var targets = new List<TrainingMuscleVolumeTarget>();
        var notes = new List<string>();
        foreach (var (muscle, priority) in chosen)
        {
            var baseline = DefaultOf(muscle);
            if (priority == TrainingMusclePriority.Emphasize)
            {
                var target = Math.Min(EmphasisTargetCeiling, baseline + tolerance);
                targets.Add(new TrainingMuscleVolumeTarget(muscle, Priority: 1, TargetWeeklyPrimarySets: target));
                notes.Add($"{muscle}: énfasis, objetivo de {target:0} series efectivas por semana (dosis normal {baseline:0}).");
            }
            else
            {
                var target = Math.Max(MaintenanceFloorSets, Math.Ceiling(baseline / 2m));
                targets.Add(new TrainingMuscleVolumeTarget(muscle, Priority: 0, TargetWeeklyPrimarySets: target, Maintenance: true));
                notes.Add($"{muscle}: mantenimiento, objetivo de {target:0} series efectivas por semana (dosis normal {baseline:0}).");
            }
        }
        return new(targets, [], notes);
    }
}
