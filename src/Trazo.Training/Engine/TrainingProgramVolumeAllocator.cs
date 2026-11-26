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
    // The first lift of a strength session is its main lift: it may reach five sets (3-5 is the usual strength dose
    // for the lift being trained), while the remaining exercises keep the three-set ceiling.
    private const int MaximumSetsForStrengthPrimaryLift = 5;
    // A session that uses less than this share of the time the user offered may raise its compound lifts up to the
    // weekly ceiling of the muscles involved (never beyond it).
    private const decimal TimeFillUtilisation = 0.70m;
    // No default allocation takes a muscle beyond this weekly effective volume, whether it is the target of the
    // exercise or a synergist. Only an explicit professional target can go higher.
    private const decimal AbsoluteWeeklySetCeiling = 20m;
    // Duration model: reps x 2.5 s + rest between sets + 45 s transition per exercise + 5 min
    // session overhead (warm-up). It decides how many extra sets fit in a session.
    private const decimal SecondsPerRepEstimate = 2.5m;
    private const int TransitionSecondsPerExercise = 45;
    private const int SessionOverheadMinutes = 5;
    // Non-hypertrophy default targets shrink linearly when weekly time is below 180 min, never
    // below 65 % of the baseline; explicit professional targets are never reduced.
    private const decimal WeeklyMinutesForFullDefaultVolume = 180m;
    private const decimal MinimumDefaultVolumeTimeFactor = 0.65m;
    /// <summary>Half-width of the range around an explicit weekly target (20 means 18-22), whatever the experience.</summary>
    public const decimal ExplicitHypertrophyTargetToleranceSets = 2m;

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
        int maximumSessionDurationMinutes,
        IReadOnlySet<string>? accessoryExerciseIds = null,
        decimal weeksPerGroup = 1m,
        int? sessionsPerWeekOverride = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var errors = ValidatePreferences(preferences);
        if (errors.Count > 0)
            return new(sessions, Array.Empty<string>(), errors);

        if (weeksPerGroup <= 0m)
            throw new ArgumentOutOfRangeException(nameof(weeksPerGroup), "A group of sessions spans a positive number of weeks.");
        // A group is normally one week. A rotation window (see TrainingSessionRotation) spans K/N nominal weeks: every
        // target and ceiling is weekly, so the volume of the group is expressed per week (divided by weeksPerGroup) and
        // one added set counts for 1/weeksPerGroup of it. With a group of one week nothing changes.
        var step = 1m / weeksPerGroup;
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> WeeklyVolume(IEnumerable<TrainingSessionPrescription> group) =>
            weeksPerGroup == 1m
                ? CalculateEffectiveVolume(group)
                : CalculateEffectiveVolume(group).ToDictionary(pair => pair.Key, pair => pair.Value / weeksPerGroup);
        var sessionsPerWeek = sessionsPerWeekOverride ?? sessions.GroupBy(session => session.WeekNumber)
            .Select(group => group.Count())
            .DefaultIfEmpty(3)
            .Max();
        var defaultVolumeTimeFactor = goal == TrainingGoal.Hypertrophy
            ? 1m
            : DefaultVolumeTimeFactor(sessionsPerWeek, maximumSessionDurationMinutes);
        var targets = BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes);
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
                    Sets = Math.Clamp(exercise.Sets, MinimumSetsPerExercise, MaximumSetsFor(exercise, goal, accessoryExerciseIds is not null))
                }).ToArray()
            });
        foreach (var week in output.Keys.Select(key => key.WeekNumber).Distinct().Order())
        {
            var weekKeys = output.Keys.Where(key => key.WeekNumber == week).OrderBy(key => key.Day).ToArray();
            var weekSessions = weekKeys.Select(key => output[key]).ToArray();
            var stimulatedMuscles = weekSessions.SelectMany(x => x.Exercises)
                .SelectMany(x => x.StimulatedMuscles())
                .Distinct()
                .ToArray();
            // A target muscle that the draft does not train at all is reported, not silently dropped.
            var applicableTargets = targets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var primaryVolume = WeeklyVolume(weekSessions);

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
                        if (exercise.Sets >= MaximumSetsFor(exercise, goal, accessoryExerciseIds is not null))
                            continue;
                        var addedDuration = AdditionalSetSeconds(exercise);
                        if (duration + addedDuration > maximumSessionDurationMinutes * 60m)
                            continue;

                        var score = DeficitScore(exercise, primaryVolume, applicableTargets, goal, experience, preferences, step);
                        if (score <= 0m)
                            continue;
                        var localPrimaryVolume = exercise.StimulatedMuscles()
                            .Select(muscle => session.Exercises
                                .Where(item => item.StimulatedMuscles().Contains(muscle))
                                .Sum(item => (decimal)item.Sets))
                            .DefaultIfEmpty(0m)
                            .Average();
                        // The main lift of a strength session takes the weekly volume first, then the other compound
                        // lifts; an accessory only receives extra sets when no compound can still take one
                        // (per-exercise limit, a muscle at its ceiling or session time).
                        var rank = AllocationRank(exercise, goal, accessoryExerciseIds);
                        var candidate = new AllocationCandidate(sessionIndex, exerciseIndex, score, localPrimaryVolume, rank);
                        if (best is null ||
                            candidate.Rank > best.Rank ||
                            (candidate.Rank == best.Rank &&
                             (candidate.Score > best.Score ||
                              (candidate.Score == best.Score && candidate.LocalPrimaryVolume < best.LocalPrimaryVolume))))
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
                primaryVolume = WeeklyVolume(weekSessions);
            }

            // Time fill: the weekly targets are central values, so a user who offers long sessions would otherwise
            // train for half of that time. While a session stays clearly under the time offered, its compound lifts
            // take one more set at a time, provided every muscle they train stays inside its weekly ceiling.
            if (accessoryExerciseIds is not null)
            {
                var limit = maximumSessionDurationMinutes * 60m;
                while (true)
                {
                    (int Session, int Exercise)? pick = null;
                    var pickKey = (Rank: int.MaxValue, Utilisation: decimal.MaxValue, Sets: int.MaxValue);
                    for (var sessionIndex = 0; sessionIndex < weekSessions.Length; sessionIndex++)
                    {
                        var session = weekSessions[sessionIndex];
                        var duration = EstimateDuration(session);
                        if (duration >= limit * TimeFillUtilisation)
                            continue;
                        for (var exerciseIndex = 0; exerciseIndex < session.Exercises.Count; exerciseIndex++)
                        {
                            var exercise = session.Exercises[exerciseIndex];
                            if (accessoryExerciseIds.Contains(exercise.ExerciseId) ||
                                exercise.Sets >= MaximumSetsFor(exercise, goal, true) ||
                                duration + AdditionalSetSeconds(exercise) > limit)
                                continue;
                            var tracked = exercise.StimulatedMuscles()
                                .Where(applicableTargets.ContainsKey)
                                .ToArray();
                            if (tracked.Length == 0 ||
                                IsAtCeiling(exercise, primaryVolume, applicableTargets, goal, experience, preferences, addedSets: step, step: step))
                                continue;
                            // This extra volume is optional, so in hypertrophy (where arms carry a full target) it
                            // also stops at the ceiling of the synergists: more pressing must not push the triceps
                            // beyond their range just because there is time left.
                            if (goal == TrainingGoal.Hypertrophy && tracked.Any(muscle =>
                                    primaryVolume.GetValueOrDefault(muscle) + step >
                                    applicableTargets[muscle] + GetUpperHeadroom(goal, experience, preferences, muscle, applicableTargets[muscle])))
                                continue;
                            // The main lifts of a strength plan first; then the least used session and, inside it,
                            // the exercise with fewer sets, then the session order.
                            var rank = goal == TrainingGoal.Strength && exercise.Order == 1 ? 0 : 1;
                            var key = (Rank: rank, Utilisation: duration / limit, Sets: exercise.Sets);
                            if (key.CompareTo(pickKey) < 0)
                            {
                                pickKey = key;
                                pick = (sessionIndex, exerciseIndex);
                            }
                        }
                    }

                    if (pick is null)
                        break;

                    var filled = weekSessions[pick.Value.Session];
                    var filledExercises = filled.Exercises.ToArray();
                    filledExercises[pick.Value.Exercise] = filledExercises[pick.Value.Exercise] with
                    {
                        Sets = filledExercises[pick.Value.Exercise].Sets + 1,
                        SelectionRationale = AppendAllocationRationale(filledExercises[pick.Value.Exercise].SelectionRationale)
                    };
                    weekSessions[pick.Value.Session] = filled with { Exercises = filledExercises };
                    primaryVolume = WeeklyVolume(weekSessions);
                }
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

    /// <summary>
    /// Ceiling for one exercise. In a generated plan (the generator tells the allocator which exercises are
    /// accessories) the main lift of a strength session may reach five sets; everything else keeps the shared ceiling.
    /// </summary>
    internal static int MaximumSetsFor(TrainingExercisePrescription exercise, TrainingGoal goal, bool generatedPlan) =>
        generatedPlan && goal == TrainingGoal.Strength && exercise.Order == 1
            ? MaximumSetsForStrengthPrimaryLift
            : GetMaximumSetsPerExercise(goal);

    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> CalculateEffectiveVolume(
        IEnumerable<TrainingSessionPrescription> sessions)
    {
        var volume = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(x => x.Exercises))
        {
            foreach (var muscle in exercise.StimulatedMuscles())
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
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        decimal step = 1m)
    {
        // Targets refer to effective sets. A compound may continue contributing while all
        // its stimulated muscles remain inside their accepted ranges, even if one has passed
        // its central target. Stop before the next set would exceed any upper bound.
        var primaryMuscles = exercise.StimulatedMuscles()
            .Where(targets.ContainsKey)
            .ToArray();
        if (primaryMuscles.Length == 0)
            return 0m;

        // A compound may contribute one additional set when another stimulated muscle is
        // exactly at its target, but must not keep accumulating volume once any target
        // muscle has exceeded its central target.
        if (IsAtCeiling(exercise, primaryVolume, targets, goal, experience, preferences, addedSets: 0m, step: step))
            return 0m;

        // Every target muscle weighs the same: hamstrings no longer yield to quadriceps, glutes and calves.
        var normalizedDeficit = primaryMuscles.Average(muscle =>
            Math.Max(0m, targets[muscle] - primaryVolume.GetValueOrDefault(muscle)) / targets[muscle]);
        return normalizedDeficit > 0m ? normalizedDeficit : 0m;
    }

    /// <summary>
    /// Whether one more set of the exercise would take a muscle past its weekly ceiling. Only the muscles the exercise
    /// lists as primary can stop it: triceps in a press or biceps in a row count towards their weekly volume, but as
    /// synergists they must not freeze the lift that trains the chest or the back (they used to, and the chest
    /// stayed under its range while the triceps sat at their ceiling). A lift with no primary target muscle is
    /// judged by every target muscle it trains.
    /// </summary>
    private static bool IsAtCeiling(
        TrainingExercisePrescription exercise,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> volume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        decimal addedSets,
        decimal step = 1m)
    {
        var limiting = exercise.PrimaryMuscleGroups.Where(targets.ContainsKey).ToArray();
        if (limiting.Length == 0)
            limiting = exercise.StimulatedMuscles()
                .Where(targets.ContainsKey).ToArray();
        if (limiting.Any(muscle =>
            {
                var ceiling = targets[muscle] + GetUpperHeadroom(goal, experience, preferences, muscle, targets[muscle]);
                return addedSets > 0m ? volume.GetValueOrDefault(muscle) + addedSets > ceiling : volume.GetValueOrDefault(muscle) >= ceiling;
            }))
            return true;

        // Synergists do not stop a lift at their own ceiling, but nothing goes past the absolute weekly ceiling. A raised
        // target (a professional one or an emphasis) moves that limit only as far as its own range: it used to remove it, and
        // the lifts that train the muscle indirectly could then pile up sets without any bound.
        return exercise.StimulatedMuscles()
            .Where(targets.ContainsKey)
            .Any(muscle => volume.GetValueOrDefault(muscle) + Math.Max(step, addedSets) >
                (HasRaisedExplicitTarget(preferences, muscle)
                    ? Math.Max(AbsoluteWeeklySetCeiling, targets[muscle] + GetUpperHeadroom(goal, experience, preferences, muscle, targets[muscle]))
                    : AbsoluteWeeklySetCeiling));
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
            .ToDictionary(pair => pair.Key, pair => pair.Value + GetUpperHeadroom(goal, experience, null, pair.Key, pair.Value));

    /// <summary>Upper bound (target + tolerance) of the weekly effective sets for this request, honoring professional targets.</summary>
    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyCeilings(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek,
        int maximumSessionDurationMinutes) =>
        BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes)
            .ToDictionary(pair => pair.Key, pair => pair.Value + GetUpperHeadroom(goal, experience, preferences, pair.Key, pair.Value));

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
            TrainingMuscleGroup.Quadriceps
        };
        var targets = primaryTargetMuscles.ToDictionary(muscle => muscle, _ => baseline);
        var supplementalBaseline = Math.Max(1m, Math.Ceiling(baseline / 2m));
        // Hamstrings: three quarters of the central target. They used to get half of it with half the allocation
        // weight, which left 2-8 weekly sets against 11-17 for the glutes.
        var hamstringsTarget = Math.Max(2m, Math.Ceiling(baseline * 0.75m));
        // Calves are a small group: half the central target, no direct work in strength plans (they do not carry
        // over to the lifts being trained) and none in short beginner plans, where the time belongs to the
        // movement patterns. A single-session allocation cannot represent a weekly budget and keeps the target.
        var shortBeginnerPlan = experience == TrainingExperience.Beginner && sessionsPerWeek >= 2 &&
            sessionsPerWeek * maximumSessionDurationMinutes < WeeklyMinutesForFullDefaultVolume;
        if (goal != TrainingGoal.Strength && !shortBeginnerPlan)
            targets[TrainingMuscleGroup.Calves] = supplementalBaseline;

        if (goal == TrainingGoal.Hypertrophy)
        {
            // In hypertrophy, all major groups (including arms and hamstrings) use the agreed
            // experience range. Abdominals have no default direct-work target; they are opt-in.
            targets[TrainingMuscleGroup.Biceps] = baseline;
            targets[TrainingMuscleGroup.Triceps] = baseline;
            targets[TrainingMuscleGroup.Hamstrings] = hamstringsTarget;
        }
        else
        {
            // Preserve the previous conservative dose for strength/general-fitness programs;
            // the new ACSM-informed hypertrophy ranges must not inflate unrelated goals.
            targets[TrainingMuscleGroup.Biceps] = supplementalBaseline;
            targets[TrainingMuscleGroup.Triceps] = supplementalBaseline;
            targets[TrainingMuscleGroup.Hamstrings] = hamstringsTarget;
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
        if (HasExplicitTarget(preferences, muscle))
            return ExplicitHypertrophyTargetToleranceSets;

        return DefaultTargetTolerance(experience);
    }

    /// <summary>Half-width of the agreed default planning range: beginner 8–12, intermediate 10–16, advanced 12–20.</summary>
    internal static decimal DefaultTargetTolerance(TrainingExperience experience) => experience switch
    {
        TrainingExperience.Beginner => 2m,
        TrainingExperience.Intermediate => 3m,
        _ => 4m
    };

    /// <summary>
    /// Sets a muscle may receive above its central target (the weekly ceiling is target + headroom). In hypertrophy it
    /// is the tolerance of the agreed range. Strength and general fitness had none, so their plans stopped at the
    /// minimum dose however much time the user offered; they now have a margin that only the time fill uses.
    /// Glutes work in every squat, hinge and single-leg lift and reach any ceiling long before quadriceps and
    /// hamstrings reach their target, which froze those lifts at two sets: their margin is wider.
    /// </summary>
    private static bool HasExplicitTarget(IReadOnlyList<TrainingMuscleVolumeTarget>? preferences, TrainingMuscleGroup muscle) =>
        (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Any(preference => preference.MuscleGroup == muscle && !preference.Learned &&
                (preference.TargetWeeklyPrimarySets is not null || preference.Priority > 0));

    /// <summary>
    /// An explicit target that RAISES the dose (a professional target or an emphasis) may exceed the absolute ceiling. A maintenance
    /// target lowers the dose of a muscle, so it never lifts that ceiling: the presses that also train a maintained triceps stay bounded.
    /// </summary>
    private static bool HasRaisedExplicitTarget(IReadOnlyList<TrainingMuscleVolumeTarget>? preferences, TrainingMuscleGroup muscle) =>
        (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Any(preference => preference.MuscleGroup == muscle && !preference.Maintenance && !preference.Learned &&
                (preference.TargetWeeklyPrimarySets is not null || preference.Priority > 0));

    private static decimal GetUpperHeadroom(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        TrainingMuscleGroup muscle,
        decimal target)
    {
        var isExplicit = HasExplicitTarget(preferences, muscle);
        // The glutes' wider margin never goes past the absolute weekly ceiling.
        var glutesLimit = Math.Max(0m, AbsoluteWeeklySetCeiling - target);
        if (goal == TrainingGoal.Hypertrophy)
        {
            var tolerance = GetTargetTolerance(goal, experience, preferences, muscle);
            return muscle == TrainingMuscleGroup.Glutes && !isExplicit ? Math.Min(tolerance * 2m, glutesLimit) : tolerance;
        }

        if (isExplicit)
            return 0m;
        // Strength: up to twice the central target (8 -> 16 weekly sets for the muscles of a lift trained three
        // times a week at 4-5 sets). General fitness: up to three times the target and never more than eight sets
        // above it (4 -> 12), enough for three sets of each exercise in a full-body week.
        decimal Headroom(decimal central) => goal == TrainingGoal.Strength ? central : Math.Min(central * 2m, 8m);
        if (muscle != TrainingMuscleGroup.Glutes)
            return Headroom(target);
        // The squat family is bounded by the quadriceps ceiling and the hinge family by the hamstrings ceiling, so
        // the glutes never need more than both together.
        var hamstringsTarget = Math.Max(2m, Math.Ceiling(target * 0.75m));
        return Math.Min(Headroom(target) + hamstringsTarget + Headroom(hamstringsTarget), glutesLimit);
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
            : $"{rationale} Ajuste de series por {marker} (motor 0.10.0).";
    }

    private static int AllocationRank(
        TrainingExercisePrescription exercise, TrainingGoal goal, IReadOnlySet<string>? accessoryExerciseIds)
    {
        if (accessoryExerciseIds is null || accessoryExerciseIds.Contains(exercise.ExerciseId)) return 0;
        return goal == TrainingGoal.Strength && exercise.Order == 1 ? 2 : 1;
    }

    private sealed record AllocationCandidate(
        int SessionIndex, int ExerciseIndex, decimal Score, decimal LocalPrimaryVolume, int Rank);
}

/// <summary>
/// A target value is the central weekly effective-set objective. The historical property name
/// TargetWeeklyPrimarySets is retained to preserve existing API request compatibility.
/// </summary>
public sealed record TrainingMuscleVolumeTarget(
    TrainingMuscleGroup MuscleGroup,
    int Priority = 0,
    decimal? TargetWeeklyPrimarySets = null,
    // A maintenance target LOWERS the dose of a muscle. It must not be read as a preference to favour that muscle
    // (exercise order, hamstring slot), so those consumers ignore it.
    bool Maintenance = false,
    // A target learned from the user's history only LOWERS the dose of a muscle that has the default priority. It is a lower weekly
    // target, not an explicit professional or user target, so it must not change the tolerance of the muscle nor lift any ceiling.
    bool Learned = false);

public sealed record TrainingVolumeAllocationResult(
    IReadOnlyList<TrainingSessionPrescription> Sessions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
