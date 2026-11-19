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

/// <summary>The weekly target of a muscle before learning, and the limits a learned change must respect (all in effective sets).</summary>
public sealed record TrainingMuscleBaseline(
    TrainingMuscleGroup Muscle, TrainingMusclePriority Priority, decimal Target, decimal Floor, decimal Ceiling);

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

    /// <summary>
    /// The weekly target each configurable muscle has BEFORE any learning from the user's history, with the limits inside which a
    /// learned adjustment may move it. It is the single place where those limits come from, so the longitudinal layer never
    /// invents its own: Emphasize never goes under the normal dose nor above <see cref="EmphasisTargetCeiling"/>, Maintain never
    /// goes up, Grow stays inside the default range (never above 20).
    /// </summary>
    public static IReadOnlyList<TrainingMuscleBaseline> Baselines(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyDictionary<TrainingMuscleGroup, TrainingMusclePriority>? priorities,
        int sessionsPerWeek,
        int maximumSessionMinutes)
    {
        if (goal != TrainingGoal.Hypertrophy) return [];
        var defaults = TrainingProgramVolumeAllocator.GetWeeklyTargets(goal, experience, null, sessionsPerWeek, maximumSessionMinutes);
        var tolerance = TrainingProgramVolumeAllocator.DefaultTargetTolerance(experience);
        var result = new List<TrainingMuscleBaseline>();
        foreach (var muscle in Configurable)
        {
            if (!defaults.TryGetValue(muscle, out var normal)) continue;
            var priority = priorities is not null && priorities.TryGetValue(muscle, out var chosen) ? chosen : TrainingMusclePriority.Grow;
            switch (priority)
            {
                case TrainingMusclePriority.Emphasize:
                {
                    var target = Math.Min(EmphasisTargetCeiling, normal + tolerance);
                    result.Add(new(muscle, priority, target, Math.Min(target, normal), EmphasisTargetCeiling));
                    break;
                }
                case TrainingMusclePriority.Maintain:
                {
                    var target = Math.Max(MaintenanceFloorSets, Math.Ceiling(normal / 2m));
                    result.Add(new(muscle, priority, target, MaintenanceFloorSets, target));
                    break;
                }
                default:
                    result.Add(new(muscle, priority, normal,
                        Math.Max(MaintenanceFloorSets, Math.Ceiling(normal * GrowFloorShare)), Math.Min(20m, normal + tolerance)));
                    break;
            }
        }
        return result;
    }

    /// <summary>Lowest share of the normal dose a learned reduction may reach for a muscle with the default priority (product limit).</summary>
    public const decimal GrowFloorShare = 0.6m;

    /// <param name="learnedTargets">
    /// Weekly targets the longitudinal layer proposes for muscles whose history justifies a change (absent = no change). They are
    /// clamped here to the limits of <see cref="Baselines"/>, so a learned value can never override a priority or a safety ceiling.
    /// </param>
    public static TrainingMusclePriorityResult Build(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyDictionary<TrainingMuscleGroup, TrainingMusclePriority>? priorities,
        int sessionsPerWeek,
        int maximumSessionMinutes,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal>? learnedTargets = null)
    {
        var chosen = (priorities ?? new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>())
            .Where(pair => pair.Value != TrainingMusclePriority.Grow).OrderBy(pair => pair.Key).ToArray();
        var limits = learnedTargets is { Count: > 0 }
            ? Baselines(goal, experience, priorities, sessionsPerWeek, maximumSessionMinutes).ToDictionary(b => b.Muscle)
            : new Dictionary<TrainingMuscleGroup, TrainingMuscleBaseline>();
        // A learned target only counts for a muscle that has a baseline, and is brought inside that baseline's limits.
        var learned = (learnedTargets ?? new Dictionary<TrainingMuscleGroup, decimal>())
            .Where(pair => limits.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => Math.Clamp(Math.Round(pair.Value), limits[pair.Key].Floor, limits[pair.Key].Ceiling));
        if (chosen.Length == 0 && learned.Count == 0) return new([], [], []);

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
                var target = learned.TryGetValue(muscle, out var adjusted) ? adjusted : Math.Min(EmphasisTargetCeiling, baseline + tolerance);
                targets.Add(new TrainingMuscleVolumeTarget(muscle, Priority: 1, TargetWeeklyPrimarySets: target));
                notes.Add($"{muscle}: énfasis, objetivo de {target:0} series efectivas por semana (dosis normal {baseline:0}).");
            }
            else
            {
                var target = learned.TryGetValue(muscle, out var adjusted) ? adjusted : Math.Max(MaintenanceFloorSets, Math.Ceiling(baseline / 2m));
                targets.Add(new TrainingMuscleVolumeTarget(muscle, Priority: 0, TargetWeeklyPrimarySets: target, Maintenance: true));
                notes.Add($"{muscle}: mantenimiento, objetivo de {target:0} series efectivas por semana (dosis normal {baseline:0}).");
            }
        }
        // Muscles with the default priority whose history justifies a different dose: an explicit target, like any other.
        foreach (var (muscle, target) in learned.Where(pair => chosen.All(c => c.Key != pair.Key)).OrderBy(pair => pair.Key))
        {
            targets.Add(new TrainingMuscleVolumeTarget(muscle, Priority: 0, TargetWeeklyPrimarySets: target));
            notes.Add($"{muscle}: objetivo de {target:0} series efectivas por semana ajustado a tu respuesta.");
        }
        return new(targets, [], notes);
    }
}
