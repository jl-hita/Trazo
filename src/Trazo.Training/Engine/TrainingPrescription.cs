namespace Trazo.Training;

/// <summary>
/// Versioned, structured prescription contract for newly generated training plans.
/// Exercise names and catalog versions are snapshots so later catalog edits cannot silently rewrite history.
/// </summary>
public sealed record TrainingProgramPrescription(
    string EngineVersion,
    string Goal,
    DateOnly StartDate,
    int DurationWeeks,
    IReadOnlyList<TrainingSessionPrescription> Sessions,
    TrainingProgramQualityReport? QualityReport = null,
    TrainingProgramHistory? History = null);

/// <summary>Comparison with the previous block of the same plan (only filled by the server on previews/generation).</summary>
public sealed record TrainingProgramHistory(
    int PreviousBlockExerciseCount,
    bool ContinuesPreviousBlock,
    IReadOnlyList<TrainingHistoryExercise> Exercises);

/// <summary>Status is "kept" (also in the previous block), "new" (not used before) or "dropped" (used before, not now).</summary>
public sealed record TrainingHistoryExercise(string ExerciseId, string Name, string Status);

public sealed record TrainingSessionPrescription(
    int WeekNumber,
    DayOfWeek Day,
    string Title,
    int EstimatedDurationMinutes,
    IReadOnlyList<TrainingExercisePrescription> Exercises);

public sealed record TrainingExercisePrescription(
    string ExerciseId,
    string CatalogVersion,
    string ExerciseNameSnapshot,
    int Order,
    int Sets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRepetitionsInReserve,
    int RestSeconds,
    TrainingProgressionRule ProgressionRule,
    string SelectionRationale,
    IReadOnlyList<TrainingMuscleGroup> PrimaryMuscleGroups,
    IReadOnlyList<TrainingMuscleGroup> SecondaryMuscleGroups,
    decimal? SmallestLoadIncrementKg = null,
    decimal? MaximumLoadIncreasePercent = null,
    int? WarmupSets = null,
    string? WarmupNote = null,
    decimal? TargetLoadKg = null,
    string? LoadBasis = null);

/// <summary>Controlled vocabulary for reviewed exercise muscle metadata.</summary>
public enum TrainingMuscleGroup
{
    Chest,
    Lats,
    UpperBack,
    Deltoids,
    Biceps,
    Triceps,
    Forearms,
    Abdominals,
    Obliques,
    ErectorSpinae,
    Glutes,
    Quadriceps,
    Hamstrings,
    Calves,
    Adductors,
    Abductors,
    HipFlexors,
    Serratus,
    TibialisAnterior,
    RotatorCuff
}

public enum TrainingProgressionRule
{
    None,
    DoubleProgression,
    IncreaseLoadWithinLimits
}

/// <summary>
/// Validates structural invariants before a generated plan can be persisted or presented.
/// It intentionally does not claim that an otherwise valid plan is physiologically optimal.
/// </summary>
public static class TrainingPrescriptionValidator
{
    public static IReadOnlyList<string> Validate(TrainingProgramPrescription? plan)
    {
        var errors = new List<string>();
        if (plan is null)
        {
            errors.Add("El plan es obligatorio.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(plan.EngineVersion) || plan.EngineVersion.Length > 40)
            errors.Add("La versión del motor debe tener entre 1 y 40 caracteres.");
        if (string.IsNullOrWhiteSpace(plan.Goal) || plan.Goal.Length > 80)
            errors.Add("El objetivo debe tener entre 1 y 80 caracteres.");
        if (plan.DurationWeeks is < 1 or > 52)
            errors.Add("La duración del plan debe estar entre 1 y 52 semanas.");
        if (plan.Sessions is null || plan.Sessions.Count == 0)
        {
            errors.Add("El plan debe contener al menos una sesión.");
            return errors;
        }

        var sessionKeys = new HashSet<(int Week, DayOfWeek Day)>();
        for (var sessionIndex = 0; sessionIndex < plan.Sessions.Count; sessionIndex++)
        {
            var session = plan.Sessions[sessionIndex];
            var prefix = $"Sesión {sessionIndex + 1}";

            if (session.WeekNumber < 1 || session.WeekNumber > plan.DurationWeeks)
                errors.Add($"{prefix}: la semana debe estar dentro de la duración del plan.");
            if (!Enum.IsDefined(session.Day))
                errors.Add($"{prefix}: el día de la semana no es válido.");
            if (!sessionKeys.Add((session.WeekNumber, session.Day)))
                errors.Add($"{prefix}: hay más de una sesión asignada al mismo día de esa semana.");
            if (string.IsNullOrWhiteSpace(session.Title) || session.Title.Length > 160)
                errors.Add($"{prefix}: el título debe tener entre 1 y 160 caracteres.");
            if (session.EstimatedDurationMinutes is < 1 or > 180)
                errors.Add($"{prefix}: la duración estimada debe estar entre 1 y 180 minutos.");
            if (session.Exercises is null || session.Exercises.Count == 0)
            {
                errors.Add($"{prefix}: debe contener al menos un ejercicio.");
                continue;
            }
            if (session.Exercises.Count > 20)
                errors.Add($"{prefix}: no puede contener más de 20 ejercicios.");

            var exerciseOrders = new HashSet<int>();
            for (var exerciseIndex = 0; exerciseIndex < session.Exercises.Count; exerciseIndex++)
            {
                var exercise = session.Exercises[exerciseIndex];
                var exercisePrefix = $"{prefix}, ejercicio {exerciseIndex + 1}";

                if (string.IsNullOrWhiteSpace(exercise.ExerciseId) || exercise.ExerciseId.Length > 100)
                    errors.Add($"{exercisePrefix}: el identificador estable del ejercicio es obligatorio (máximo 100 caracteres).");
                if (string.IsNullOrWhiteSpace(exercise.CatalogVersion) || exercise.CatalogVersion.Length > 40)
                    errors.Add($"{exercisePrefix}: la versión del catálogo es obligatoria (máximo 40 caracteres).");
                if (string.IsNullOrWhiteSpace(exercise.ExerciseNameSnapshot) || exercise.ExerciseNameSnapshot.Length > 160)
                    errors.Add($"{exercisePrefix}: el nombre histórico del ejercicio es obligatorio (máximo 160 caracteres).");
                if (exercise.Order < 1 || !exerciseOrders.Add(exercise.Order))
                    errors.Add($"{exercisePrefix}: el orden debe ser positivo y no repetirse.");
                if (exercise.Sets is < 1 or > 10)
                    errors.Add($"{exercisePrefix}: las series deben estar entre 1 y 10.");
                if (exercise.MinRepetitions is < 1 or > 30 ||
                    exercise.MaxRepetitions is < 1 or > 30 ||
                    exercise.MinRepetitions > exercise.MaxRepetitions)
                    errors.Add($"{exercisePrefix}: el rango de repeticiones debe ser válido entre 1 y 30.");
                if (exercise.TargetRepetitionsInReserve is < 0 or > 5)
                    errors.Add($"{exercisePrefix}: el RIR objetivo debe estar entre 0 y 5.");
                if (exercise.RestSeconds is < 0 or > 600)
                    errors.Add($"{exercisePrefix}: el descanso debe estar entre 0 y 600 segundos.");
                if (exercise.SmallestLoadIncrementKg is <= 0)
                    errors.Add($"{exercisePrefix}: el incremento mínimo de carga debe ser positivo cuando se especifica.");
                if (exercise.MaximumLoadIncreasePercent is <= 0 or > 10)
                    errors.Add($"{exercisePrefix}: el incremento máximo de carga debe ser mayor que 0 y no superar el 10 % cuando se especifica.");
                if (exercise.WarmupSets is < 0 or > 3 || exercise.WarmupNote?.Length > 300)
                    errors.Add($"{exercisePrefix}: las series de aproximación deben estar entre 0 y 3 y la nota admite hasta 300 caracteres.");
                if (exercise.TargetLoadKg is <= 0 or > 1000 || exercise.LoadBasis?.Length > 300)
                    errors.Add($"{exercisePrefix}: la carga objetivo debe ser positiva (máximo 1000 kg) y su motivo admite hasta 300 caracteres.");
                if (!Enum.IsDefined(exercise.ProgressionRule))
                    errors.Add($"{exercisePrefix}: la regla de progresión no es válida.");
                if (string.IsNullOrWhiteSpace(exercise.SelectionRationale) || exercise.SelectionRationale.Length > 500)
                    errors.Add($"{exercisePrefix}: debe documentarse el motivo de selección (máximo 500 caracteres).");
                if (exercise.PrimaryMuscleGroups is null || exercise.PrimaryMuscleGroups.Count == 0)
                    errors.Add($"{exercisePrefix}: debe declarar al menos un grupo muscular principal revisado.");
                if (exercise.SecondaryMuscleGroups is null)
                    errors.Add($"{exercisePrefix}: la lista de grupos musculares secundarios es obligatoria, aunque esté vacía.");
                if (exercise.PrimaryMuscleGroups is not null)
                {
                    if (exercise.PrimaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                        errors.Add($"{exercisePrefix}: contiene un grupo muscular principal no válido.");
                    if (exercise.PrimaryMuscleGroups.Distinct().Count() != exercise.PrimaryMuscleGroups.Count)
                        errors.Add($"{exercisePrefix}: repite grupos musculares principales.");
                }
                if (exercise.SecondaryMuscleGroups is not null)
                {
                    if (exercise.SecondaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                        errors.Add($"{exercisePrefix}: contiene un grupo muscular secundario no válido.");
                    if (exercise.SecondaryMuscleGroups.Distinct().Count() != exercise.SecondaryMuscleGroups.Count)
                        errors.Add($"{exercisePrefix}: repite grupos musculares secundarios.");
                    if (exercise.PrimaryMuscleGroups is not null &&
                        exercise.PrimaryMuscleGroups.Intersect(exercise.SecondaryMuscleGroups).Any())
                        errors.Add($"{exercisePrefix}: un grupo muscular no puede ser principal y secundario a la vez.");
                }
            }
        }

        return errors;
    }
}


/// <summary>
/// Exercise-specific stimulus audit for weekly hypertrophy volume. Primary muscles always count.
/// A secondary muscle counts only when the exercise loads it dynamically through a meaningful
/// range of motion; stabilizers, grip-only work and incidental co-contraction do not count.
/// This is a conservative, explicit curation of the 97-exercise RepDB generator catalog, not
/// a universal biomechanical classifier.
/// </summary>
internal static class TrainingExerciseStimulus
{
    private static readonly IReadOnlyDictionary<string, TrainingMuscleGroup[]> MeaningfulSecondary =
        new Dictionary<string, TrainingMuscleGroup[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Horizontal/vertical pressing: elbow extension is a meaningful loaded action.
            ["bench-press"] = [TrainingMuscleGroup.Triceps],
            ["db-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["incline-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["incline-db-press"] = [TrainingMuscleGroup.Triceps],
            ["smith-machine-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["smith-machine-incline-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["chest-press-machine"] = [TrainingMuscleGroup.Triceps],
            ["push-up"] = [TrainingMuscleGroup.Triceps],
            ["knee-push-ups"] = [TrainingMuscleGroup.Triceps],
            ["incline-push-ups"] = [TrainingMuscleGroup.Triceps],
            ["ohp"] = [TrainingMuscleGroup.Triceps],
            ["dumbbell-shoulder-press"] = [TrainingMuscleGroup.Triceps],
            ["machine-shoulder-press"] = [TrainingMuscleGroup.Triceps],

            // Rows load elbow flexors and lats dynamically; vertical pulls load elbow flexors.
            ["barbell-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["single-arm-db-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-db-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-machine-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["t-bar-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-kettlebell-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["kneeling-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["one-arm-seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["v-bar-lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["assisted-pull-ups"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["pull-up"] = [TrainingMuscleGroup.Biceps],
            ["chin-ups"] = [TrainingMuscleGroup.Biceps],

            // Knee-dominant compounds: both quads and glutes can receive meaningful dynamic work.
            ["squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["front-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["goblet-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["db-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["leg-press"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["horizontal-leg-press"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["hack-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bulgarian-split-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["reverse-lunge"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["step-ups"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],

            // Hip hinges: hamstrings and glutes are loaded through hip extension.
            ["deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hex-bar-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["romanian-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["dumbbell-romanian-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],

            // Rear-delt fly/face-pull variations dynamically load the upper back as well.
            ["dumbbell-reverse-fly"] = [TrainingMuscleGroup.UpperBack],
            ["machine-reverse-fly"] = [TrainingMuscleGroup.UpperBack],
            ["face-pull"] = [TrainingMuscleGroup.UpperBack],

            // Hip-extension back extensions and loaded knee raises also train these muscles dynamically.
            ["machine-back-extension"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hanging-knee-raise"] = [TrainingMuscleGroup.HipFlexors],
            ["captains-chair-knee-raise"] = [TrainingMuscleGroup.HipFlexors],

            // TRAZO additions. Same criteria as above: a secondary muscle counts only when the exercise loads it
            // dynamically through a meaningful range of motion.
            ["wide-grip-seated-cable-row"] = [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats],
            ["chest-supported-t-bar-row"] = [TrainingMuscleGroup.Biceps],
            ["pendlay-row"] = [TrainingMuscleGroup.Biceps],
            ["weighted-pull-up"] = [TrainingMuscleGroup.Biceps],
            ["arnold-press"] = [TrainingMuscleGroup.Triceps],
            ["seated-barbell-overhead-press"] = [TrainingMuscleGroup.Triceps],
            ["push-press"] = [TrainingMuscleGroup.Triceps],
            ["z-press"] = [TrainingMuscleGroup.Triceps],
            ["pike-push-up"] = [TrainingMuscleGroup.Triceps],
            ["handstand-push-up"] = [TrainingMuscleGroup.Triceps],
            ["paused-bench-press"] = [TrainingMuscleGroup.Triceps],
            ["pause-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["zercher-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["barbell-bulgarian-split-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["pistol-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["rack-pull"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["deficit-deadlift"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["good-morning"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings],
            ["hanging-leg-raise"] = [TrainingMuscleGroup.HipFlexors],
            ["bodyweight-squat"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bodyweight-reverse-lunge"] = [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps],
            ["bent-knee-inverted-row"] = [TrainingMuscleGroup.Biceps],
            ["negative-pull-up"] = [TrainingMuscleGroup.Biceps],
            ["band-row"] = [TrainingMuscleGroup.Biceps],
            ["band-lat-pulldown"] = [TrainingMuscleGroup.Biceps],
            ["band-shoulder-press"] = [TrainingMuscleGroup.Triceps],
            ["elevated-pike-push-up"] = [TrainingMuscleGroup.Triceps]
        };

    // Explicitly audited catalog isolates/accessories whose secondary tags do not represent
    // enough dynamic stimulus to add a full hypertrophy set to another muscle.
    private static readonly HashSet<string> ReviewedWithoutMeaningfulSecondary =
    [
        "straight-arm-pulldown", "hip-thrust", "plate-loaded-glute-drive", "barbell-glute-bridge",
        "cable-fly", "db-fly", "pec-deck", "lateral-raise", "cable-lateral-raise", "shrug",
        "barbell-curl", "hammer-curl", "incline-db-curl", "preacher-curl", "cable-curl",
        "tricep-pushdown", "cable-overhead-tricep-extension", "overhead-tricep-extension", "skull-crusher",
        "leg-extension", "reverse-nordic-curl", "leg-curl", "seated-leg-curl", "nordic-hamstring-curl",
        "ball-leg-curl", "banded-standing-curl", "trx-hamstring-curl", "standing-calf-raise",
        "seated-calf-raise", "kettlebell-tibialis-raise", "cable-external-rotation", "hip-adduction",
        "standing-hip-abduction", "hip-abduction", "cable-crunch", "ab-wheel-rollout",
        "cable-pallof-press", "side-plank", "dumbbell-farmers-walk", "seated-dumbbell-lateral-raise",
        "machine-seated-crunch", "dead-bug", "machine-chest-fly", "machine-bicep-curl",
        "single-arm-plate-loaded-lateral-raise", "machine-calf-raise", "side-lying-hip-abduction",
        "side-lying-hip-adduction", "glute-bridge", "bodyweight-calf-raise",
        // TRAZO additions: the secondary tags do not add a full set to another muscle.
        "dumbbell-shrug", "band-pull-apart", "incline-bench-y-raise", "leg-press-calf-raise",
        "dumbbell-single-leg-calf-raise", "seated-dumbbell-calf-raise", "smith-machine-calf-raise",
        "barbell-standing-calf-raise", "dumbbell-front-raise", "cable-front-raise", "weighted-dip",
        "dragon-flag", "standing-ab-wheel-rollout", "single-leg-glute-bridge",
        // TRAZO seed exercises that predate the table (decision: no meaningful secondary).
        "dumbbell-curl", "parallel-bar-dips", "plank", "walking-lunge", "sumo-deadlift",
        "close-grip-bench-press", "lying-leg-raise"
    ];

    public static bool IsReviewed(string exerciseId) =>
        MeaningfulSecondary.ContainsKey(exerciseId) || ReviewedWithoutMeaningfulSecondary.Contains(exerciseId);

    public static IReadOnlySet<TrainingMuscleGroup> GetEffectiveMuscles(
        string exerciseId,
        IReadOnlyList<TrainingMuscleGroup> primary,
        IReadOnlyList<TrainingMuscleGroup> secondary)
    {
        var result = primary.ToHashSet();
        if (!MeaningfulSecondary.TryGetValue(exerciseId, out var meaningful))
            return result;

        foreach (var muscle in secondary)
            if (meaningful.Contains(muscle))
                result.Add(muscle);

        return result;
    }
}
