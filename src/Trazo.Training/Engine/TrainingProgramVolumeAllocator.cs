namespace Trazo.Training;

/// <summary>
/// Allocates weekly working sets using an exercise-specific audit of meaningful muscle stimulus.
/// A set counts fully for each muscle that receives a relevant hypertrophic stimulus; incidental
/// stabilizers and insufficient secondary contributions are excluded.
/// </summary>
public static class TrainingProgramVolumeAllocator
{
    // All constants below are internal product limits (not guideline figures).
    private const int MinimumSetsPerExercise = 1;
    private const int MaximumSetsPerExercise = 5;
    private const int MaximumSetsPerExerciseForStrength = 3;
    // Duration model: reps x 2.5 s + rest between sets + 45 s transition per exercise + 5 min
    // session overhead (warm-up). It decides how many extra sets fit in a session.
    private const decimal SecondsPerRepEstimate = 2.5m;
    private const int TransitionSecondsPerExercise = 45;
    private const int SessionOverheadMinutes = 5;
    // Non-hypertrophy default targets shrink linearly when weekly time is below 180 min, never
    // below 65 % of the baseline; explicit professional targets are never reduced.
    private const decimal WeeklyMinutesForFullDefaultVolume = 180m;
    private const decimal MinimumDefaultVolumeTimeFactor = 0.65m;
    private const decimal ExplicitHypertrophyTargetToleranceSets = 2m;

    /// <summary>
    /// Raises each exercise's sets one at a time, always choosing the exercise whose stimulated muscles
    /// have the largest normalized deficit to their weekly target, until no set fits within the
    /// per-exercise ceiling, the muscle's upper tolerance or the session time limit. Remaining
    /// deficits become warnings; the allocator never forces volume. Returns errors only for invalid
    /// preferences or when a session ends up longer than the allowed duration.
    /// </summary>
    public static TrainingVolumeAllocationResult Allocate(
        IReadOnlyList<TrainingSessionPrescription> sessions,
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int maximumSessionDurationMinutes)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var errors = ValidatePreferences(preferences);
        if (errors.Count > 0)
            return new(sessions, Array.Empty<string>(), errors);

        var sessionsPerWeek = sessions.GroupBy(session => session.WeekNumber)
            .Select(group => group.Count())
            .DefaultIfEmpty(3)
            .Max();
        var defaultVolumeTimeFactor = goal == TrainingGoal.Hypertrophy
            ? 1m
            : DefaultVolumeTimeFactor(sessionsPerWeek, maximumSessionDurationMinutes);
        var targets = BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes);
        // Product ceiling aligned with the ACSM 2026 practical summary of 2–3 sets per exercise
        // for strength. It is a ceiling, not a mandatory dose; remaining weekly deficits stay
        // visible as warnings instead of concentrating more sets on one lift.
        var maximumSetsPerExercise = GetMaximumSetsPerExercise(goal);
        var warnings = new List<string>();
        var explicitTargetMuscles = (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Where(preference => preference.TargetWeeklyPrimarySets is not null)
            .Select(preference => preference.MuscleGroup)
            .ToHashSet();
        var hasDefaultTargetMuscles = sessions.SelectMany(session => session.Exercises)
            .SelectMany(exercise => exercise.PrimaryMuscleGroups)
            .Any(muscle => !explicitTargetMuscles.Contains(muscle));
        if (defaultVolumeTimeFactor < 1m && hasDefaultTargetMuscles)
            warnings.Add($"Los objetivos predeterminados de series se ajustan de forma conservadora al tiempo semanal disponible ({sessionsPerWeek} sesiones × {maximumSessionDurationMinutes} min; factor {defaultVolumeTimeFactor:0.##}). Es una heurística de planificación, no una dosis fisiológica establecida por ACSM; los objetivos explícitos del profesional no se reducen.");
        var output = sessions.ToDictionary(
            session => (session.WeekNumber, session.Day),
            session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    Sets = Math.Clamp(exercise.Sets, MinimumSetsPerExercise, maximumSetsPerExercise)
                }).ToArray()
            });
        foreach (var week in output.Keys.Select(key => key.WeekNumber).Distinct().Order())
        {
            var weekKeys = output.Keys.Where(key => key.WeekNumber == week).OrderBy(key => key.Day).ToArray();
            var weekSessions = weekKeys.Select(key => output[key]).ToArray();
            var stimulatedMuscles = weekSessions.SelectMany(x => x.Exercises)
                .SelectMany(x => TrainingExerciseStimulus.GetEffectiveMuscles(
                    x.ExerciseId, x.PrimaryMuscleGroups, x.SecondaryMuscleGroups))
                .Distinct()
                .ToArray();
            // Calves are a priority group even when the current draft omitted direct work:
            // absence must be reported rather than silently removing the target from evaluation.
            var applicableTargets = targets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var primaryVolume = CalculateEffectiveVolume(weekSessions);

            foreach (var muscle in applicableTargets.Keys.Where(muscle => !stimulatedMuscles.Contains(muscle)))
                warnings.Add($"Semana {week}: no hay ningún ejercicio disponible con estímulo suficiente para {muscle}; no se inventa cobertura a partir de una participación incidental.");

            // Greedy allocation chooses the exercise that best addresses the largest remaining
            // normalized muscle deficit, while respecting per-exercise and per-session limits.
            // A set on a multi-primary exercise contributes to each reviewed primary muscle;
            // no extra credit is given merely because an exercise lists more muscle labels.
            while (true)
            {
                AllocationCandidate? best = null;
                for (var sessionIndex = 0; sessionIndex < weekSessions.Length; sessionIndex++)
                {
                    var session = weekSessions[sessionIndex];
                    var duration = EstimateDuration(session);
                    for (var exerciseIndex = 0; exerciseIndex < session.Exercises.Count; exerciseIndex++)
                    {
                        var exercise = session.Exercises[exerciseIndex];
                        if (exercise.Sets >= maximumSetsPerExercise)
                            continue;
                        var addedDuration = AdditionalSetSeconds(exercise);
                        if (duration + addedDuration > maximumSessionDurationMinutes * 60m)
                            continue;

                        var score = DeficitScore(exercise, primaryVolume, applicableTargets, goal, experience, preferences);
                        if (score <= 0m)
                            continue;
                        var localPrimaryVolume = TrainingExerciseStimulus.GetEffectiveMuscles(
                            exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups)
                            .Select(muscle => session.Exercises
                                .Where(item => TrainingExerciseStimulus.GetEffectiveMuscles(
                                    item.ExerciseId, item.PrimaryMuscleGroups, item.SecondaryMuscleGroups).Contains(muscle))
                                .Sum(item => (decimal)item.Sets))
                            .DefaultIfEmpty(0m)
                            .Average();
                        var candidate = new AllocationCandidate(sessionIndex, exerciseIndex, score, localPrimaryVolume);
                        if (best is null || candidate.Score > best.Score ||
                            (candidate.Score == best.Score && candidate.LocalPrimaryVolume < best.LocalPrimaryVolume))
                            best = candidate;
                    }
                }

                if (best is null)
                    break;

                var selectedSession = weekSessions[best.SessionIndex];
                var exercises = selectedSession.Exercises.ToArray();
                exercises[best.ExerciseIndex] = exercises[best.ExerciseIndex] with
                {
                    Sets = exercises[best.ExerciseIndex].Sets + 1,
                    SelectionRationale = AppendAllocationRationale(exercises[best.ExerciseIndex].SelectionRationale)
                };
                weekSessions[best.SessionIndex] = selectedSession with { Exercises = exercises };
                primaryVolume = CalculateEffectiveVolume(weekSessions);
            }

            foreach (var (muscle, target) in applicableTargets.OrderBy(x => x.Key))
            {
                if (!stimulatedMuscles.Contains(muscle))
                    continue;

                var achieved = primaryVolume.GetValueOrDefault(muscle);
                var tolerance = GetTargetTolerance(goal, experience, preferences, muscle);
                var minimum = Math.Max(0m, target - tolerance);
                if (achieved + 0.001m < minimum)
                    warnings.Add($"Semana {week}: {muscle} alcanza {achieved:0.##} series con estímulo efectivo frente al rango orientativo {minimum:0.##}–{target + tolerance:0.##}; no se fuerza más volumen porque no queda una opción compatible de ejercicio, material y tiempo que permita añadir series sin superar los límites de equilibrio muscular, series por ejercicio o duración.");
            }

            for (var index = 0; index < weekKeys.Length; index++)
            {
                var session = weekSessions[index];
                var durationMinutes = (int)Math.Ceiling(EstimateDuration(session) / 60m);
                if (durationMinutes > maximumSessionDurationMinutes)
                    return new(sessions, warnings,
                        [$"La asignación de volumen hace que '{session.Title}' requiera {durationMinutes} minutos y supere el límite de {maximumSessionDurationMinutes}."]);
                output[weekKeys[index]] = session with { EstimatedDurationMinutes = durationMinutes };
            }
        }

        return new(output.Values.OrderBy(x => x.WeekNumber).ThenBy(x => x.Day).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(), Array.Empty<string>());
    }

    /// <summary>
    /// Shared per-exercise working-set ceiling for generation and feedback-based volume increases.
    /// Strength uses three sets per exercise; other goals retain the five-set product ceiling.
    /// </summary>
    public static int GetMaximumSetsPerExercise(TrainingGoal goal) =>
        goal == TrainingGoal.Strength ? MaximumSetsPerExerciseForStrength : MaximumSetsPerExercise;

    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> CalculateEffectiveVolume(
        IEnumerable<TrainingSessionPrescription> sessions)
    {
        var volume = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(x => x.Exercises))
        {
            foreach (var muscle in TrainingExerciseStimulus.GetEffectiveMuscles(
                         exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups))
                volume[muscle] = volume.GetValueOrDefault(muscle) + exercise.Sets;
        }
        return volume;
    }

    /// <summary>
    /// Returns only the raw primary-label series count for diagnostics and backward-compatible tests.
    /// Allocation and quality decisions must use CalculateEffectiveVolume instead.
    /// </summary>
    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> CalculatePrimaryVolume(
        IEnumerable<TrainingSessionPrescription> sessions)
    {
        var volume = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(x => x.Exercises))
        foreach (var muscle in exercise.PrimaryMuscleGroups.Distinct())
            volume[muscle] = volume.GetValueOrDefault(muscle) + exercise.Sets;
        return volume;
    }

    private static decimal DeficitScore(
        TrainingExercisePrescription exercise,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> primaryVolume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        // Targets refer to effective sets. A compound may continue contributing while all
        // its stimulated muscles remain inside their accepted ranges, even if one has passed
        // its central target. Stop before the next set would exceed any upper bound.
        var primaryMuscles = TrainingExerciseStimulus.GetEffectiveMuscles(
                exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups)
            .Where(targets.ContainsKey)
            .ToArray();
        if (primaryMuscles.Length == 0)
            return 0m;

        // A compound may contribute one additional set when another stimulated muscle is
        // exactly at its target, but must not keep accumulating volume once any target
        // muscle has exceeded its central target.
        if (primaryMuscles.Any(muscle =>
                primaryVolume.GetValueOrDefault(muscle) >=
                targets[muscle] + GetTargetTolerance(goal, experience, preferences, muscle)))
            return 0m;

        var normalizedDeficit = primaryMuscles.Average(muscle =>
        {
            var deficit = Math.Max(0m, targets[muscle] - primaryVolume.GetValueOrDefault(muscle)) / targets[muscle];
            // Hamstrings remain in the hypertrophy target range, but yield accessory/compound
            // allocation priority to quads, glutes and calves when session time is constrained.
            var hamstringsPrioritized = (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
                .Any(preference => preference.MuscleGroup == TrainingMuscleGroup.Hamstrings &&
                    (preference.Priority > 0 || preference.TargetWeeklyPrimarySets is not null));
            var importance = muscle == TrainingMuscleGroup.Hamstrings && !hamstringsPrioritized ? 0.5m : 1m;
            return deficit * importance;
        });
        return normalizedDeficit > 0m ? normalizedDeficit : 0m;
    }

    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek = 3,
        int maximumSessionDurationMinutes = 60) =>
        BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes);

    /// <summary>Upper bound of the default weekly dose per muscle (target + tolerance); used to stop automatic volume increases.</summary>
    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyCeilings(TrainingGoal goal, TrainingExperience experience) =>
        BuildTargets(goal, experience, null, 3, 60)
            .ToDictionary(pair => pair.Key, pair => pair.Value + GetTargetTolerance(goal, experience, null, pair.Key));

    /// <summary>Upper bound (target + tolerance) of the weekly effective sets for this request, honoring professional targets.</summary>
    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyCeilings(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek,
        int maximumSessionDurationMinutes) =>
        BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes)
            .ToDictionary(pair => pair.Key, pair => pair.Value + GetTargetTolerance(goal, experience, preferences, pair.Key));

    internal static decimal EstimateDurationSeconds(TrainingSessionPrescription session) =>
        EstimateDuration(session);

    private static Dictionary<TrainingMuscleGroup, decimal> BuildTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek,
        int maximumSessionDurationMinutes)
    {
        var baseBaseline = goal switch
        {
            // Midpoints of the agreed planning ranges: beginner 8–12, intermediate 10–16,
            // advanced 12–20. Professional priority can raise the central target; default increases are capped at 20 unless an explicit target overrides that ceiling.
            TrainingGoal.Hypertrophy => experience switch
            {
                TrainingExperience.Beginner => 10m,
                TrainingExperience.Intermediate => 13m,
                _ => 16m
            },
            TrainingGoal.Strength => experience == TrainingExperience.Beginner ? 6m : 8m,
            _ => experience == TrainingExperience.Beginner ? 4m : 6m
        };
        // Hypertrophy targets are not silently discounted because a session is short.
        // The allocator reports the achievable dose against the target range instead.
        var timeFactor = goal == TrainingGoal.Hypertrophy
            ? 1m
            : DefaultVolumeTimeFactor(sessionsPerWeek, maximumSessionDurationMinutes);
        var baseline = Math.Max(1m, Math.Ceiling(baseBaseline * timeFactor));

        // Do not assign a universal weekly set quota to every anatomical label. The default
        // dose applies to the main muscle groups typically trained in general strength/hypertrophy
        // programs. Smaller/accessory groups (e.g. hip flexors, obliques, adductors, abductors,
        // serratus, tibialis anterior and rotator cuff) receive direct volume when the professional
        // explicitly prioritizes them, rather than creating automatic deficits in every plan.
        // Main hypertrophy groups use the same evidence-informed planning range.
        // Biceps/triceps are not assigned a smaller quota merely because compounds also train them:
        // qualifying compound work is counted in full by TrainingExerciseStimulus.
        var primaryTargetMuscles = new[]
        {
            TrainingMuscleGroup.Chest,
            TrainingMuscleGroup.Lats,
            TrainingMuscleGroup.UpperBack,
            TrainingMuscleGroup.Deltoids,
            TrainingMuscleGroup.Glutes,
            TrainingMuscleGroup.Quadriceps,
            TrainingMuscleGroup.Calves
        };
        var targets = primaryTargetMuscles.ToDictionary(muscle => muscle, _ => baseline);
        var supplementalBaseline = Math.Max(1m, Math.Ceiling(baseline / 2m));

        if (goal == TrainingGoal.Hypertrophy)
        {
            // In hypertrophy, all major groups (including arms and hamstrings) use the agreed
            // experience range. Abdominals have no default direct-work target; they are opt-in.
            targets[TrainingMuscleGroup.Biceps] = baseline;
            targets[TrainingMuscleGroup.Triceps] = baseline;
            // Preserve the previously agreed lower hamstring priority: half the central
            // target, with the same experience-specific tolerance. Professionals can override it.
            targets[TrainingMuscleGroup.Hamstrings] = Math.Max(2m, Math.Ceiling(baseline * 0.5m));
        }
        else
        {
            // Preserve the previous conservative dose for strength/general-fitness programs;
            // the new ACSM-informed hypertrophy ranges must not inflate unrelated goals.
            targets[TrainingMuscleGroup.Biceps] = supplementalBaseline;
            targets[TrainingMuscleGroup.Triceps] = supplementalBaseline;
            targets[TrainingMuscleGroup.Hamstrings] = Math.Max(2m, Math.Ceiling(baseline * 0.5m));
        }

        foreach (var preference in preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
        {
            var preferenceTarget = preference.TargetWeeklyPrimarySets ?? baseline + preference.Priority * 2m;
            // Default priority cannot silently push a hypertrophy target beyond the evidence-informed
            // 20-set planning ceiling. A professional may still enter a higher explicit target.
            if (goal == TrainingGoal.Hypertrophy && preference.TargetWeeklyPrimarySets is null)
                preferenceTarget = Math.Min(20m, preferenceTarget);
            targets[preference.MuscleGroup] = preferenceTarget;
        }
        return targets;
    }

    private static decimal GetTargetTolerance(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        TrainingMuscleGroup muscle)
    {
        if (goal != TrainingGoal.Hypertrophy)
            return 0m;

        // Explicit professional targets use a narrow +/-2 range (e.g. 20 means 18–22),
        // regardless of experience; defaults use the broader experience-specific ranges.
        if ((preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Any(preference => preference.MuscleGroup == muscle &&
                (preference.TargetWeeklyPrimarySets is not null || preference.Priority > 0)))
            return ExplicitHypertrophyTargetToleranceSets;

        // Agreed default planning ranges: beginner 8–12, intermediate 10–16, advanced 12–20.
        return experience switch
        {
            TrainingExperience.Beginner => 2m,
            TrainingExperience.Intermediate => 3m,
            _ => 4m
        };
    }

    private static decimal DefaultVolumeTimeFactor(int sessionsPerWeek, int maximumSessionDurationMinutes)
    {
        // The generator requires at least two weekly sessions. A single-session allocation is
        // also used for isolated allocator tests/drafts, so it cannot reliably represent a
        // complete weekly time budget and must retain the unscaled baseline.
        if (sessionsPerWeek < 2)
            return 1m;

        var weeklyMinutes = Math.Max(0, sessionsPerWeek) * Math.Max(0, maximumSessionDurationMinutes);
        return Math.Clamp(weeklyMinutes / WeeklyMinutesForFullDefaultVolume,
            MinimumDefaultVolumeTimeFactor, 1m);
    }

    private static IReadOnlyList<string> ValidatePreferences(IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        if (preferences is null) return Array.Empty<string>();
        var errors = new List<string>();
        var seen = new HashSet<TrainingMuscleGroup>();
        foreach (var preference in preferences)
        {
            if (!Enum.IsDefined(preference.MuscleGroup))
                errors.Add("La preferencia contiene un grupo muscular no válido.");
            else if (!seen.Add(preference.MuscleGroup))
                errors.Add($"La preferencia repite el grupo muscular {preference.MuscleGroup}.");
            if (preference.Priority is < 0 or > 3)
                errors.Add($"La prioridad de {preference.MuscleGroup} debe estar entre 0 y 3.");
            if (preference.TargetWeeklyPrimarySets is { } target &&
                (target is < 1 or > 30 || target != decimal.Truncate(target)))
                errors.Add($"El objetivo semanal de {preference.MuscleGroup} debe ser un entero entre 1 y 30 series efectivas estimadas.");
        }
        return errors;
    }

    private static decimal EstimateDuration(TrainingSessionPrescription session)
    {
        var seconds = SessionOverheadMinutes * 60m;
        foreach (var exercise in session.Exercises)
        {
            var averageRepetitions = (exercise.MinRepetitions + exercise.MaxRepetitions) / 2m;
            seconds += exercise.Sets * averageRepetitions * SecondsPerRepEstimate;
            seconds += Math.Max(0, exercise.Sets - 1) * exercise.RestSeconds;
            seconds += TransitionSecondsPerExercise;
        }
        return seconds;
    }

    private static decimal AdditionalSetSeconds(TrainingExercisePrescription exercise) =>
        ((exercise.MinRepetitions + exercise.MaxRepetitions) / 2m) * SecondsPerRepEstimate + exercise.RestSeconds;

    private static string AppendAllocationRationale(string rationale)
    {
        const string marker = "volumen semanal individualizado";
        return rationale.Contains(marker, StringComparison.OrdinalIgnoreCase)
            ? rationale
            : $"{rationale} Ajuste de series por {marker} (motor 0.8.0).";
    }

    private sealed record AllocationCandidate(
        int SessionIndex, int ExerciseIndex, decimal Score, decimal LocalPrimaryVolume);
}

/// <summary>
/// A target value is the central weekly effective-set objective. The historical property name
/// TargetWeeklyPrimarySets is retained to preserve existing API request compatibility.
/// </summary>
public sealed record TrainingMuscleVolumeTarget(
    TrainingMuscleGroup MuscleGroup,
    int Priority = 0,
    decimal? TargetWeeklyPrimarySets = null);

public sealed record TrainingVolumeAllocationResult(
    IReadOnlyList<TrainingSessionPrescription> Sessions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
