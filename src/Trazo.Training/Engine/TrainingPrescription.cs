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
    string? LoadBasis = null,
    // The secondary muscles that count as a set. Null only in prescriptions stored before the field existed.
    IReadOnlyList<TrainingMuscleGroup>? EffectiveSecondaryMuscleGroups = null);

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
/// Which muscles an exercise really stimulates: its primary muscles plus the secondary ones with a meaningful dynamic
/// contribution. That decision is reviewed catalog data (<see cref="TrainingExerciseCandidate.EffectiveSecondaryMuscleGroups"/>,
/// copied into every prescription), never something deduced from the exercise's identifier. A set counts fully or not at all:
/// it is a planning convention, not a measurement of stimulus.
/// </summary>
public static class TrainingStimulus
{
    public static IReadOnlySet<TrainingMuscleGroup> Of(
        IEnumerable<TrainingMuscleGroup> primary, IEnumerable<TrainingMuscleGroup>? effectiveSecondary)
    {
        var result = primary.ToHashSet();
        foreach (var muscle in effectiveSecondary ?? [])
            result.Add(muscle);
        return result;
    }

    public static IReadOnlySet<TrainingMuscleGroup> StimulatedMuscles(this TrainingExercisePrescription exercise) =>
        Of(exercise.PrimaryMuscleGroups, exercise.EffectiveSecondaryMuscleGroups);

    public static IReadOnlySet<TrainingMuscleGroup> StimulatedMuscles(this TrainingExerciseCandidate exercise) =>
        Of(exercise.PrimaryMuscleGroups, exercise.EffectiveSecondaryMuscleGroups);

    /// <summary>The same by name, for the parts of the application that measure dose and must agree with what the generator planned.</summary>
    public static IReadOnlyCollection<string> EffectiveMuscles(IEnumerable<string> primary, IEnumerable<string> effectiveSecondary)
    {
        static IEnumerable<TrainingMuscleGroup> Parse(IEnumerable<string> names) =>
            names.Select(name => Enum.TryParse<TrainingMuscleGroup>(name, out var muscle) ? (TrainingMuscleGroup?)muscle : null)
                .Where(muscle => muscle is not null).Select(muscle => muscle!.Value);
        return Of(Parse(primary), Parse(effectiveSecondary)).Select(muscle => muscle.ToString()).ToList();
    }
}
