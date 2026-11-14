namespace Trazo.Training;

/// <summary>
/// First deterministic draft generator. It only selects from explicitly supplied, reviewed catalog metadata.
/// It does not infer exercise properties from names or automatically publish/apply the generated plan.
///
/// Flow of <see cref="Generate"/> (one call produces ONE block/mesocycle of 1–52 weeks):
///  1. Validate the request and the catalog (structural errors are returned, never thrown).
///  2. Pick the weekly split (<see cref="BuildSplit"/>) and, per session, one exercise per movement
///     pattern from the reviewed catalog (goal, level, equipment, exclusions; best SelectionPriority,
///     optionally rotated within a small curated pool, see <see cref="RotateCuratedPool"/>).
///  3. Add 1–4 accessory exercises per session according to the session time budget and the
///     weekly muscle deficit (<see cref="AddOptionalAccessoryExercises"/>).
///  4. <see cref="TrainingProgramVolumeAllocator"/> distributes working sets towards weekly targets
///     under time and per-exercise limits.
///  5. Warm-up guidance, then either the explicit deload or the intra-block progression
///     (introductory week). A deload is never applied automatically.
///  6. Quality report (non-blocking warnings) and structural validation of the final plan.
/// Every threshold in this file is an internal product heuristic unless a comment says otherwise;
/// none is a figure prescribed by ACSM. Macrocycle phases are NOT built here (see
/// <see cref="TrainingMacrocycleDraftBuilder"/>); the professional decides how blocks are chained.
/// </summary>
public static class TrainingProgramGenerator
{
    private const decimal SecondsPerRepEstimate = 2.5m;
    private const int TransitionSecondsPerExercise = 45;
    private const int WarmupAndTransitionsMinutes = 5;
    private const decimal QuadAccessoryGluteTargetBufferSets = 6m;

    public static TrainingProgramGenerationResult Generate(
        TrainingProgramGenerationRequest request,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        var inputErrors = ValidateRequest(request);
        if (inputErrors.Count > 0)
            return new(null, inputErrors);

        var candidateErrors = ValidateCatalog(catalog);
        if (candidateErrors.Count > 0)
            return new(null, candidateErrors);

        var days = request.TrainingDays
            .Distinct()
            .OrderBy(day => ((int)day + 6) % 7)
            .ToArray();
        var split = BuildSplit(days.Length, request.Split);
        var excluded = new HashSet<string>(request.ExcludedExerciseIds, StringComparer.OrdinalIgnoreCase);
        var equipment = new HashSet<string>(
            request.AvailableEquipment
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeEquipmentToken),
            StringComparer.OrdinalIgnoreCase);
        var sessions = new List<TrainingSessionPrescription>();
        for (var week = 1; week <= request.DurationWeeks; week++)
        {
            // Keep exercise selection stable across weeks to support measurable progression.
            // Variety is managed within each week's sessions, not across the mesocycle.
            var selectedThisWeek = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
            {
                var sessionTemplate = split[dayIndex];
                var requiredPatterns = sessionTemplate.Patterns;
                var prescriptions = new List<TrainingExercisePrescription>();
                for (var patternIndex = 0; patternIndex < requiredPatterns.Count; patternIndex++)
                {
                    var pattern = requiredPatterns[patternIndex];
                    // Core is already trained indirectly by the compound movements in this
                    // product's default strength/hypertrophy routines. Direct core work is
                    // intentionally opt-in, so skip this optional template pattern without
                    // changing the remaining exercise-selection flow.
                    if (pattern == TrainingMovementPattern.Core && !HasExplicitCorePriority(request.MuscleVolumeTargets))
                        continue;

                    var compatibleCandidates = catalog
                        .Where(x => (x.IsReviewed || x.IsOwnedByCurrentProfessional)
                            && x.MovementPattern == pattern
                            && x.SuitableGoals.Contains(request.Goal)
                            && (int)x.MinimumExperience <= (int)request.Experience
                            && (equipment.Count == 0 || HasRequiredEquipment(x.RequiredEquipment, equipment))
                            && !excluded.Contains(x.ExerciseId))
                        .OrderByDescending(x => x.SelectionPriority)
                        .ThenBy(x => x.ExerciseId, StringComparer.Ordinal)
                        .ToArray();

                    // Vertical and horizontal pulling both train the lats/upper back. Do not
                    // reject an otherwise complete beginner plan merely because a limited
                    // equipment profile cannot perform a vertical pull (e.g. no cable or
                    // assisted-pull-up machine). Substitute a horizontal row when this session
                    // has no horizontal pull; if it already has one, retain that exposure and
                    // omit the unavailable direction rather than duplicate the same pattern.
                    if (compatibleCandidates.Length == 0 && pattern == TrainingMovementPattern.VerticalPull)
                    {
                        var hasHorizontalPull = requiredPatterns.Take(patternIndex)
                            .Contains(TrainingMovementPattern.HorizontalPull);
                        if (hasHorizontalPull)
                            continue;

                        compatibleCandidates = catalog
                            .Where(x => (x.IsReviewed || x.IsOwnedByCurrentProfessional)
                                && x.MovementPattern == TrainingMovementPattern.HorizontalPull
                                && x.SuitableGoals.Contains(request.Goal)
                                && (int)x.MinimumExperience <= (int)request.Experience
                                && (equipment.Count == 0 || HasRequiredEquipment(x.RequiredEquipment, equipment))
                                && !excluded.Contains(x.ExerciseId))
                            .OrderByDescending(x => x.SelectionPriority)
                            .ThenBy(x => x.ExerciseId, StringComparer.Ordinal)
                            .ToArray();
                    }

                    // TRAZO: an exercise never appears twice in one session. When the only exercise that fits this pattern is
                    // already in the session (typical with little equipment: one row serves both pulling patterns), the
                    // pattern is covered and is skipped instead of repeating the exercise.
                    var notYetInSession = compatibleCandidates
                        .Where(x => !prescriptions.Any(p => string.Equals(p.ExerciseId, x.ExerciseId, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                    if (compatibleCandidates.Length > 0 && notYetInSession.Length == 0)
                        continue;
                    compatibleCandidates = notYetInSession;

                    // Prefer a different exercise elsewhere in this week's plan when possible.
                    // Never penalize an exercise for being selected in previous weeks: continuity
                    // across a mesocycle supports progression and performance tracking.
                    var candidate = RotateCuratedPool(compatibleCandidates, request.RotationIndex ?? 0, request.PreviousBlockExerciseIds, request.IsDeload)
                        .FirstOrDefault(x => !selectedThisWeek.Contains(x.ExerciseId))
                        ?? compatibleCandidates.FirstOrDefault();

                    if (candidate is null)
                        return Failure(BuildCandidateDiagnostic(pattern, request, catalog, equipment, excluded));

                    selectedThisWeek.Add(candidate.ExerciseId);

                    var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(request.Goal, request.Experience, week, days.Length, pattern, isPrimaryLift: patternIndex == 0);
                    prescriptions.Add(new TrainingExercisePrescription(
                        candidate.ExerciseId,
                        candidate.CatalogVersion,
                        candidate.Name,
                        prescriptions.Count + 1,
                        sets,
                        minReps,
                        maxReps,
                        rir,
                        rest,
                        progression,
                        $"Patrón {pattern}; metadatos {(candidate.IsReviewed ? "revisados" : "aportados por el profesional propietario")}; compatible con objetivo, experiencia, material y exclusiones.",
                        candidate.PrimaryMuscleGroups.ToArray(),
                        candidate.SecondaryMuscleGroups.ToArray()));
                }

                AddOptionalAccessoryExercises(
                    request, catalog, equipment, excluded, sessions,
                    prescriptions, week, days[dayIndex], sessionTemplate.Title, days.Length);

                // Final duration is calculated after weekly sets have been allocated.
                sessions.Add(new TrainingSessionPrescription(
                    week,
                    days[dayIndex],
                    sessionTemplate.Title,
                    WarmupAndTransitionsMinutes,
                    prescriptions));
            }
        }

        var allocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, request.Goal, request.Experience, request.MuscleVolumeTargets,
            request.MaximumSessionDurationMinutes);
        if (!allocation.Succeeded)
            return new(null, allocation.Errors);

        // Allocate volume before reordering. The allocator resolves exact ties by the current
        // exercise order; sorting earlier would silently move sets between exercises even though
        // the orderer itself only changes Order. Apply presentation/fatigue-aware ordering only
        // after weekly set allocation is complete.
        var orderedAllocatedSessions = allocation.Sessions.Select(session => session with
        {
            Exercises = TrainingSessionExerciseOrderer.Reorder(
                session.Exercises, catalog, request.MuscleVolumeTargets)
        }).ToArray();
        var sessionsWithWarmups = ApplyLoadReferences(AddWarmupGuidance(orderedAllocatedSessions), request.LoadReferences);
        var prescribedSessions = request.IsDeload
            ? ApplyConservativeDeload(sessionsWithWarmups)
            : request.Goal is TrainingGoal.Hypertrophy or TrainingGoal.Strength
                ? ApplyHypertrophyRamp(sessionsWithWarmups, request, catalog)
                : ApplyIntraBlockProgression(sessionsWithWarmups, request.DurationWeeks, request.ContinuesPreviousBlock);
        var plan = new TrainingProgramPrescription(
            "0.9.0",
            request.Goal.ToString(),
            request.StartDate,
            request.DurationWeeks,
            prescribedSessions);
        var qualityReport = TrainingProgramQualityAnalyzer.Analyze(plan);
        var oneSetExposures = allocation.Sessions.SelectMany(session => session.Exercises)
            .Count(exercise => exercise.Sets <= 1);
        var deloadWarnings = request.IsDeload
            ? new[]
            {
                "Semana/fase de descarga activa: las series se reducen aproximadamente entre un tercio y la mitad (redondeo hacia arriba, mínimo una por ejercicio) y el objetivo de esfuerzo se aleja dos RIR. Es una heurística conservadora revisable, no una pauta establecida por ACSM.",
                oneSetExposures > 0
                    ? $"{oneSetExposures} exposiciones ya estaban prescritas a una serie y mantienen esa serie; en ellas la descarga actúa mediante el mayor margen de RIR, no mediante un recorte adicional de series."
                    : null
            }.Where(warning => warning is not null).Select(warning => warning!).ToArray()
            : Array.Empty<string>();
        plan = plan with
        {
            QualityReport = qualityReport with
            {
                Warnings = qualityReport.Warnings.Concat(allocation.Warnings).Concat(deloadWarnings).Distinct(StringComparer.Ordinal).ToArray()
            }
        };
        var validationErrors = TrainingPrescriptionValidator.Validate(plan);
        return validationErrors.Count == 0
            ? new(plan, Array.Empty<string>())
            : new(null, validationErrors);

        TrainingProgramGenerationResult Failure(string error) => new(null, [error]);
    }

    /// <summary>
    /// Automatic intra-block ramp for hypertrophy and strength blocks (internal product heuristic, not a
    /// guideline figure; general fitness keeps the legacy scheme). Effort rises across the block (RIR offset +1 in week 1 down to -1 in the
    /// last week, floored at RIR 1 for isolation work and RIR 2 for compounds) and, in blocks of
    /// four or more weeks, volume rises too: weeks 1-2 trim one set from exercises with three or
    /// more sets, the middle weeks keep the allocated dose and the last week adds at most one set
    /// per muscle, never above the muscle's weekly ceiling, the per-exercise limit or the session
    /// duration. It is never a deload. A block that continues a previous one starts with a smaller
    /// reserve bump and no set trimming.
    /// </summary>
    private static IReadOnlyList<TrainingSessionPrescription> ApplyHypertrophyRamp(
        IReadOnlyList<TrainingSessionPrescription> sessions,
        TrainingProgramGenerationRequest request,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog)
    {
        var weeks = request.DurationWeeks;
        if (weeks < 2) return sessions;

        var patternById = catalog
            .GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().MovementPattern, StringComparer.OrdinalIgnoreCase);
        var sessionsPerWeek = sessions.GroupBy(x => x.WeekNumber).Select(g => g.Count()).DefaultIfEmpty(3).Max();
        var ceilings = TrainingProgramVolumeAllocator.GetWeeklyCeilings(
            request.Goal, request.Experience, request.MuscleVolumeTargets,
            sessionsPerWeek, request.MaximumSessionDurationMinutes);
        var maxSets = TrainingProgramVolumeAllocator.GetMaximumSetsPerExercise(request.Goal);
        // Volume ramp (trim in weeks 1-2, extra set in the last week) is hypertrophy-only; strength
        // progresses through load, so it only gets the effort ramp and the legacy week-1 trim.
        var rampVolume = request.Goal == TrainingGoal.Hypertrophy;
        var continues = request.ContinuesPreviousBlock;

        var result = new List<TrainingSessionPrescription>();
        foreach (var weekGroup in sessions.GroupBy(x => x.WeekNumber).OrderBy(g => g.Key))
        {
            var week = weekGroup.Key;
            var offset = HypertrophyRirOffset(week, weeks, continues);
            var trimSet = !continues && weeks >= 4 && (rampVolume ? week <= 2 : week == 1);
            var weekSessions = weekGroup.OrderBy(x => x.Day).Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise =>
                {
                    var isIsolation = patternById.TryGetValue(exercise.ExerciseId, out var pattern) &&
                        (pattern == TrainingMovementPattern.Accessory || pattern == TrainingMovementPattern.Core);
                    var floor = isIsolation && request.Experience != TrainingExperience.Beginner ? 1m : 2m;
                    return exercise with
                    {
                        Sets = trimSet && exercise.Sets >= 3 ? exercise.Sets - 1 : exercise.Sets,
                        TargetRepetitionsInReserve = exercise.TargetRepetitionsInReserve is { } rir
                            ? ApplyRirOffset(rir, offset, floor)
                            : null
                    };
                }).ToArray()
            }).ToArray();

            if (rampVolume && weeks >= 4 && week == weeks)
                AddPeakWeekSets(weekSessions, ceilings, maxSets, request.MaximumSessionDurationMinutes);

            result.AddRange(weekSessions.Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    SelectionRationale = exercise.SelectionRationale +
                        " Rampa del bloque: esfuerzo y volumen progresivos (heurística interna)."
                }).ToArray()
            }));
        }
        return result;
    }

    private static decimal HypertrophyRirOffset(int week, int weeks, bool continues)
    {
        decimal offset;
        if (week == weeks && weeks >= 3) offset = -1m;
        else if (weeks == 2) offset = week == 1 ? 0.5m : 0m;
        else if (week == 1) offset = 1m;
        else if (weeks == 3) offset = 0m;
        else offset = Math.Round(0.5m * (1m - (week - 2m) / (weeks - 3m)) * 2m, MidpointRounding.ToEven) / 2m;

        if (!continues) return offset;
        // A continuing block is already adapted: smaller introductory reserve, same final push.
        return offset > 0m ? Math.Min(offset, week == 1 ? 0.5m : 0m) : offset;
    }

    private static decimal ApplyRirOffset(decimal rir, decimal offset, decimal floor)
    {
        var target = rir + offset;
        if (offset < 0m) target = Math.Max(target, Math.Min(rir, floor));
        return Math.Clamp(target, 0m, 5m);
    }

    private static void AddPeakWeekSets(
        TrainingSessionPrescription[] weekSessions,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> ceilings,
        int maxSets,
        int maximumSessionDurationMinutes)
    {
        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(weekSessions)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var boosted = new HashSet<TrainingMuscleGroup>();
        for (var sessionIndex = 0; sessionIndex < weekSessions.Length; sessionIndex++)
        {
            foreach (var exercise in weekSessions[sessionIndex].Exercises.OrderBy(x => x.Order).ToArray())
            {
                if (exercise.Sets >= maxSets) continue;
                var tracked = TrainingExerciseStimulus.GetEffectiveMuscles(
                        exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups)
                    .Where(ceilings.ContainsKey)
                    .ToArray();
                if (tracked.Length == 0 || tracked.Any(boosted.Contains)) continue;
                if (tracked.Any(muscle => volume.GetValueOrDefault(muscle) + 1m > ceilings[muscle])) continue;

                var session = weekSessions[sessionIndex];
                var candidateExercises = session.Exercises
                    .Select(x => x.Order == exercise.Order ? x with { Sets = x.Sets + 1 } : x)
                    .ToArray();
                if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(session with { Exercises = candidateExercises }) >
                    maximumSessionDurationMinutes * 60m)
                    continue;

                var updated = session with { Exercises = candidateExercises };
                weekSessions[sessionIndex] = updated with
                {
                    EstimatedDurationMinutes = (int)Math.Ceiling(
                        TrainingProgramVolumeAllocator.EstimateDurationSeconds(updated) / 60m)
                };
                foreach (var muscle in tracked)
                {
                    boosted.Add(muscle);
                    volume[muscle] = volume.GetValueOrDefault(muscle) + 1m;
                }
            }
        }
    }

    /// <summary>
    /// Internal product heuristic (not an ACSM-prescribed scheme): in blocks of three or more weeks
    /// the first week is introductory (+1 RIR; +0.5 RIR in week 2 of blocks of five or more) and,
    /// in blocks of four or more weeks, exercises with three or more sets lose one set in week 1.
    /// Later weeks keep the allocated weekly dose, so effort and volume rise across the block.
    /// It is never a deload and never reduces any exercise below two sets.
    /// </summary>
    private static IReadOnlyList<TrainingSessionPrescription> ApplyIntraBlockProgression(
        IReadOnlyList<TrainingSessionPrescription> sessions, int durationWeeks, bool continuesPreviousBlock = false)
    {
        if (durationWeeks < 3) return sessions;
        return sessions.Select(session =>
        {
            // A block that directly continues a training block is already adapted: no set trimming and
            // a smaller reserve bump, instead of repeating the full "introduction" week (sawtooth volume).
            var rirOffset = continuesPreviousBlock
                ? (session.WeekNumber == 1 ? 0.5m : 0m)
                : session.WeekNumber == 1 ? 1m
                : session.WeekNumber == 2 && durationWeeks >= 5 ? 0.5m : 0m;
            var trimSet = !continuesPreviousBlock && session.WeekNumber == 1 && durationWeeks >= 4;
            if (rirOffset == 0m && !trimSet) return session;
            return session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    Sets = trimSet && exercise.Sets >= 3 ? exercise.Sets - 1 : exercise.Sets,
                    TargetRepetitionsInReserve = exercise.TargetRepetitionsInReserve is null
                        ? null
                        : Math.Min(5m, exercise.TargetRepetitionsInReserve.Value + rirOffset),
                    SelectionRationale = exercise.SelectionRationale +
                        " Semana de introducción del bloque: mayor reserva de repeticiones (umbral interno de producto, no pauta ACSM)."
                }).ToArray()
            };
        }).ToArray();
    }

    // A pattern keeps at most this many "best fit" exercises in its rotation pool, and only those
    // whose curated SelectionPriority is within PoolPriorityWindow points of the best one.
    // Internal product criteria (not literature thresholds): rotation must never reach a clearly
    // worse exercise just to add variety.
    private const int RotationPoolSize = 3;
    private const int PoolPriorityWindow = 20;

    /// <summary>
    /// Orders candidates so that rotation moves among the few best-suited exercises of a pattern.
    /// Index 0 returns the original priority order (previous behaviour). Each higher index starts
    /// one position later in the pool, so successive mesocycles use different but equally suitable
    /// exercises while the exercises stay constant inside one block (progression needs continuity).
    /// Candidates outside the pool keep their order after the pool, as a fallback only.
    /// </summary>
    private static IReadOnlyList<TrainingExerciseCandidate> RotateCuratedPool(
        IReadOnlyList<TrainingExerciseCandidate> orderedByPriority, int rotationIndex,
        IReadOnlyList<string>? previousBlockExerciseIds = null, bool isDeload = false)
    {
        if (orderedByPriority.Count < 2) return orderedByPriority;
        var best = orderedByPriority[0].SelectionPriority;
        var pool = orderedByPriority.Take(RotationPoolSize)
            .Where(candidate => best - candidate.SelectionPriority <= PoolPriorityWindow).ToList();
        if (previousBlockExerciseIds is { Count: > 0 })
        {
            // History-aware rotation: what the previous block really used decides, not the block number
            // (a one-week deload would otherwise shift the rotation). A training block moves to an equally
            // suitable exercise it did not use; a deload keeps the previous block's exercises.
            var used = previousBlockExerciseIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ordered = pool.OrderBy(candidate => used.Contains(candidate.ExerciseId) == isDeload ? 0 : 1).ToList();
            return ordered.Concat(orderedByPriority.Skip(pool.Count)).ToArray();
        }
        if (rotationIndex <= 0) return orderedByPriority;
        var offset = rotationIndex % pool.Count;
        return pool.Skip(offset).Concat(pool.Take(offset)).Concat(orderedByPriority.Skip(pool.Count)).ToArray();
    }

    /// <summary>
    /// Adds warm-up guidance without touching working volume. An exercise gets one or two very easy
    /// approach sets only when it trains a primary muscle that no earlier exercise of the session
    /// already worked as a primary mover (e.g. bench press -> cable fly: the chest is already warm,
    /// but lateral raises still need their own). The session's first exercise gets two sets, the rest
    /// one. Core-only exercises are skipped. These sets are not counted in weekly volume and their
    /// time is covered by the fixed warm-up/transition allowance of the session estimate.
    /// This is an internal product convention, not a figure taken from a guideline.
    /// </summary>
    public static IReadOnlyList<TrainingSessionPrescription> AddWarmupGuidanceForTesting(
        IReadOnlyList<TrainingSessionPrescription> sessions) => AddWarmupGuidance(sessions);

    private static IReadOnlyList<TrainingSessionPrescription> AddWarmupGuidance(
        IReadOnlyList<TrainingSessionPrescription> sessions) =>
        sessions.Select(session =>
        {
            var warmed = new HashSet<TrainingMuscleGroup>();
            var isFirst = true;
            var exercises = session.Exercises.OrderBy(exercise => exercise.Order).Select(exercise =>
            {
                var coreOnly = exercise.PrimaryMuscleGroups.All(muscle =>
                    muscle is TrainingMuscleGroup.Abdominals or TrainingMuscleGroup.Obliques);
                var cold = exercise.PrimaryMuscleGroups.Where(muscle => !warmed.Contains(muscle)).ToArray();
                warmed.UnionWith(exercise.PrimaryMuscleGroups);
                if (coreOnly || cold.Length == 0) return exercise;
                var sets = isFirst ? 2 : 1;
                isFirst = false;
                return exercise with
                {
                    WarmupSets = sets,
                    WarmupNote = $"Haz {sets} serie(s) de aproximación muy fáciles y lejos del fallo antes de las series de trabajo, para preparar {string.Join(", ", cold)}. No cuentan como series de trabajo."
                };
            }).ToArray();
            return session with { Exercises = exercises };
        }).ToArray();

    /// <summary>
    /// Prescribes a working load for week 1 only, and only for exercises with a recorded reference
    /// (see <see cref="TrainingLoadEstimator"/>). Later weeks are left without a number because the
    /// right load depends on how week 1 goes; the progression engine proposes it session by session.
    /// Exercises without history keep no load rather than receiving an invented one.
    /// </summary>
    private static IReadOnlyList<TrainingSessionPrescription> ApplyLoadReferences(
        IReadOnlyList<TrainingSessionPrescription> sessions,
        IReadOnlyList<TrainingLoadReference>? references)
    {
        if (references is null || references.Count == 0) return sessions;
        var byExercise = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ExerciseId))
            .GroupBy(reference => reference.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        return sessions.Select(session => session.WeekNumber != 1 ? session : session with
        {
            Exercises = session.Exercises.Select(exercise =>
            {
                if (!byExercise.TryGetValue(exercise.ExerciseId, out var reference)) return exercise;
                var sets = Enumerable.Repeat(
                    new TrainingExecutedSet("working", reference.WeightKg, reference.Repetitions, reference.Rir, true),
                    Math.Clamp(reference.Sets, 1, 10)).ToArray();
                var increment = exercise.SmallestLoadIncrementKg ?? TrainingLoadEstimator.DefaultLoadIncrementKg;
                var suggestion = TrainingLoadEstimator.Suggest(
                    sets, exercise.MinRepetitions, exercise.MaxRepetitions,
                    exercise.TargetRepetitionsInReserve, increment);
                return suggestion is null
                    ? exercise
                    : exercise with
                    {
                        TargetLoadKg = suggestion.LoadKg,
                        LoadBasis = suggestion.Basis,
                        SmallestLoadIncrementKg = increment
                    };
            }).ToArray()
        }).ToArray();
    }

    private static IReadOnlyList<TrainingSessionPrescription> ApplyConservativeDeload(
        IReadOnlyList<TrainingSessionPrescription> sessions) =>
        sessions.Select(session => session with
        {
            Exercises = session.Exercises.Select(exercise => exercise with
            {
                Sets = Math.Max(1, (int)Math.Ceiling(exercise.Sets * 0.5m)),
                TargetRepetitionsInReserve = exercise.TargetRepetitionsInReserve is null
                    ? null
                    : Math.Min(5m, exercise.TargetRepetitionsInReserve.Value + 2m),
                // A deload is a recovery phase, not another opportunity to progress load.
                // The progression service must return NotApplicable until normal training resumes.
                ProgressionRule = TrainingProgressionRule.None,
                SelectionRationale = exercise.SelectionRationale + " Descarga activa: reducción conservadora de series, mayor reserva respecto al fallo y progresión de carga pausada."
            }).ToArray()
        }).ToArray();

    // A professional's explicit priority/target always overrides the default core exclusion.
    private static bool HasExplicitCorePriority(IReadOnlyList<TrainingMuscleVolumeTarget>? preferences) =>
        (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Any(preference =>
                preference.MuscleGroup is TrainingMuscleGroup.Abdominals or TrainingMuscleGroup.Obliques &&
                (preference.Priority > 0 || preference.TargetWeeklyPrimarySets is not null));

    // Exclude only exercises whose reviewed primary targets are the abdominal wall.
    // Compounds that list abs/obliques as secondary muscles remain fully eligible.
    private static bool IsDirectCoreExercise(TrainingExerciseCandidate candidate) =>
        candidate.PrimaryMuscleGroups.Any(muscle =>
            muscle is TrainingMuscleGroup.Abdominals or TrainingMuscleGroup.Obliques);

    internal static bool IsAccessoryGoalCompatible(
        TrainingExerciseCandidate candidate,
        TrainingGoal requestedGoal)
    {
        if (candidate.SuitableGoals.Contains(requestedGoal)) return true;

        // Hypertrophy-oriented isolation work is also a valid accessory in strength plans.
        // Keep this exception limited to accessories; primary compound patterns still require
        // an explicit goal match.
        return requestedGoal == TrainingGoal.Strength
            && candidate.MovementPattern == TrainingMovementPattern.Accessory
            && candidate.SuitableGoals.Contains(TrainingGoal.Hypertrophy);
    }

    internal static string NormalizeEquipmentToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // RepDB uses underscores while the application catalog uses hyphenated tokens.
        return string.Join("-", value.Trim().ToLowerInvariant()
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Equipment check: every required token must be available. "machine" is the generic gym
    // option and is interchangeable with any "<something>-machine" token in both directions.
    internal static bool HasRequiredEquipment(
        IReadOnlyList<string> requiredEquipment,
        IReadOnlySet<string> availableEquipment)
    {
        if (availableEquipment.Count == 0) return true;

        return requiredEquipment.All(required =>
        {
            var token = NormalizeEquipmentToken(required);
            if (availableEquipment.Contains(token)) return true;

            // "machine" is the broad gym-equipment option. It satisfies a specific machine
            // requirement; a specific machine also satisfies a generic machine requirement.
            if (token.Equals("machine", StringComparison.OrdinalIgnoreCase))
                return availableEquipment.Any(value =>
                    value.Equals("machine", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith("-machine", StringComparison.OrdinalIgnoreCase));

            return token.EndsWith("-machine", StringComparison.OrdinalIgnoreCase)
                && availableEquipment.Contains("machine");
        });
    }

    // Explains WHY no exercise was found by counting candidates after each filter step, so the
    // professional can see whether review status, goal, level, equipment or exclusions removed them.
    private static string BuildCandidateDiagnostic(
        TrainingMovementPattern pattern,
        TrainingProgramGenerationRequest request,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlySet<string> equipment,
        IReadOnlySet<string> excluded)
    {
        var reviewed = catalog.Where(x => x.IsReviewed || x.IsOwnedByCurrentProfessional).ToArray();
        var matchingPattern = reviewed.Where(x => x.MovementPattern == pattern).ToArray();
        var matchingGoal = matchingPattern.Where(x => x.SuitableGoals.Contains(request.Goal)).ToArray();
        var matchingExperience = matchingGoal.Where(x => (int)x.MinimumExperience <= (int)request.Experience).ToArray();
        var matchingEquipment = matchingExperience.Where(x => equipment.Count == 0 || HasRequiredEquipment(x.RequiredEquipment, equipment)).ToArray();
        var matchingExclusions = matchingEquipment.Where(x => !excluded.Contains(x.ExerciseId)).ToArray();

        return $"No hay ejercicios compatibles para el patrón '{pattern}'. " +
            $"Candidatos: catálogo={catalog.Count}, revisados/propios={reviewed.Length}, patrón={matchingPattern.Length}, " +
            $"objetivo={matchingGoal.Length}, experiencia={matchingExperience.Length}, material={matchingEquipment.Length}, " +
            $"tras exclusiones={matchingExclusions.Length}. " +
            "Comprueba aprobación, patrón, objetivo, nivel mínimo, material disponible y ejercicios excluidos.";
    }

    private static IReadOnlyList<string> ValidateRequest(TrainingProgramGenerationRequest request)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(request.Goal)) errors.Add("El objetivo de entrenamiento no es válido.");
        if (!Enum.IsDefined(request.Experience)) errors.Add("El nivel de experiencia no es válido.");
        if (request.DurationWeeks is < 1 or > 52) errors.Add("La duración debe estar entre 1 y 52 semanas.");
        if (request.IsDeload && request.DurationWeeks != 1)
            errors.Add("Una descarga activa debe durar exactamente una semana; divide la fase antes de generar.");
        if (request.TrainingDays is null || request.TrainingDays.Count is < 2 or > 6)
            errors.Add("Selecciona entre 2 y 6 días de entrenamiento por semana.");
        else
        {
            if (request.TrainingDays.Any(day => !Enum.IsDefined(day)))
                errors.Add("La disponibilidad contiene un día no válido.");
            if (request.TrainingDays.Distinct().Count() != request.TrainingDays.Count)
                errors.Add("No se puede repetir un día de entrenamiento.");
        }
        if (!Enum.IsDefined(request.Split)) errors.Add("La distribución semanal seleccionada no es válida.");
        else if (request.TrainingDays is not null)
        {
            var dayCount = request.TrainingDays.Count;
            var supportedDayCount = request.Split switch
            {
                TrainingProgramSplit.Automatic => dayCount is >= 2 and <= 4,
                TrainingProgramSplit.FullBody => dayCount is >= 2 and <= 4,
                TrainingProgramSplit.UpperLower => dayCount is 2 or 4,
                TrainingProgramSplit.PushPullLegs => dayCount is 3 or 5 or 6,
                _ => false
            };
            if (!supportedDayCount)
                errors.Add(request.Split switch
                {
                    TrainingProgramSplit.FullBody => "Cuerpo completo admite de 2 a 4 días; cambia la disponibilidad o elige otra distribución.",
                    TrainingProgramSplit.UpperLower => "Torso/Pierna admite 2 o 4 días; cambia la disponibilidad o elige otra distribución.",
                    TrainingProgramSplit.PushPullLegs => "Push/Pull/Legs admite 3, 5 o 6 días; cambia la disponibilidad o elige otra distribución.",
                    _ => "La distribución automática admite entre 2 y 4 días."
                });
        }
        if (request.MaximumSessionDurationMinutes is < 35 or > 180)
            errors.Add("La duración máxima de sesión debe estar entre 35 y 180 minutos.");
        if (request.AvailableEquipment is null)
            errors.Add("Debe indicarse el equipamiento disponible, aunque sea una lista vacía.");
        if (request.ExcludedExerciseIds is null)
            errors.Add("La lista de ejercicios excluidos es obligatoria, aunque esté vacía.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateCatalog(IReadOnlyCollection<TrainingExerciseCandidate> catalog)
    {
        var errors = new List<string>();
        if (catalog.Count == 0)
        {
            errors.Add("El catálogo de ejercicios está vacío.");
            return errors;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exercise in catalog)
        {
            if (string.IsNullOrWhiteSpace(exercise.ExerciseId) || exercise.ExerciseId.Length > 100)
                errors.Add("Todos los ejercicios del catálogo deben tener un ID estable de hasta 100 caracteres.");
            else if (!seenIds.Add(exercise.ExerciseId))
                errors.Add($"El catálogo contiene el ID duplicado '{exercise.ExerciseId}'.");
            if (string.IsNullOrWhiteSpace(exercise.CatalogVersion) || exercise.CatalogVersion.Length > 40)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' no tiene una versión de catálogo válida.");
            if (string.IsNullOrWhiteSpace(exercise.Name) || exercise.Name.Length > 160)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' no tiene un nombre válido.");
            if (!Enum.IsDefined(exercise.MovementPattern))
                errors.Add($"El ejercicio '{exercise.ExerciseId}' tiene un patrón de movimiento no válido.");
            if (!Enum.IsDefined(exercise.MinimumExperience))
                errors.Add($"El ejercicio '{exercise.ExerciseId}' tiene un nivel mínimo de experiencia no válido.");
            if (exercise.SelectionPriority is < 0 or > 100)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe tener una prioridad de selección entre 0 y 100.");
            if (exercise.PrimaryMuscleGroups is null || exercise.PrimaryMuscleGroups.Count == 0)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar al menos un grupo muscular principal revisado.");
            if (exercise.SecondaryMuscleGroups is null)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar grupos secundarios, aunque la lista esté vacía.");
            if (exercise.PrimaryMuscleGroups is not null)
            {
                if (exercise.PrimaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un grupo muscular principal no válido.");
                if (exercise.PrimaryMuscleGroups.Distinct().Count() != exercise.PrimaryMuscleGroups.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite grupos musculares principales.");
            }
            if (exercise.SecondaryMuscleGroups is not null)
            {
                if (exercise.SecondaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un grupo muscular secundario no válido.");
                if (exercise.SecondaryMuscleGroups.Distinct().Count() != exercise.SecondaryMuscleGroups.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite grupos musculares secundarios.");
                if (exercise.PrimaryMuscleGroups is not null &&
                    exercise.PrimaryMuscleGroups.Intersect(exercise.SecondaryMuscleGroups).Any())
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' declara un músculo como principal y secundario.");
            }

            if (exercise.RequiredEquipment is null || exercise.SuitableGoals is null || exercise.SuitableGoals.Count == 0)
            {
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar material y al menos un objetivo compatible.");
            }
            else
            {
                if (exercise.RequiredEquipment.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un requisito de material vacío.");
                if (exercise.SuitableGoals.Any(goal => !Enum.IsDefined(goal)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un objetivo compatible no válido.");
                if (exercise.SuitableGoals.Distinct().Count() != exercise.SuitableGoals.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite objetivos compatibles.");
            }
        }

        return errors;
    }

    private static void AddOptionalAccessoryExercises(
        TrainingProgramGenerationRequest request,
        IEnumerable<TrainingExerciseCandidate> catalog,
        HashSet<string> equipment,
        HashSet<string> excluded,
        IReadOnlyList<TrainingSessionPrescription> previousSessions,
        List<TrainingExercisePrescription> currentExercises,
        int week,
        DayOfWeek day,
        string title,
        int daysPerWeek)
    {
        var targets = TrainingProgramVolumeAllocator.GetWeeklyTargets(
            request.Goal, request.Experience, request.MuscleVolumeTargets,
            daysPerWeek, request.MaximumSessionDurationMinutes);
        var catalogCandidates = catalog.ToArray();
        var isFullBody = title.Contains("completo", StringComparison.OrdinalIgnoreCase);
        // Short sessions must protect time for the core movement patterns. Accessory slots
        // scale with the available duration instead of being appended in the same quantity
        // to a 35-minute session and a 90-minute session.
        var slots = request.MaximumSessionDurationMinutes <= 40
            ? 1
            : request.MaximumSessionDurationMinutes <= 65
                ? 2
                : (isFullBody ? 4 : 3);
        IReadOnlySet<TrainingMuscleGroup>? allowedMuscles = title.Contains("superior", StringComparison.OrdinalIgnoreCase)
            ? UpperBodyAccessoryMuscles
            : title.Contains("inferior", StringComparison.OrdinalIgnoreCase) || title.Contains("pierna", StringComparison.OrdinalIgnoreCase)
                ? LowerBodyAccessoryMuscles
                : title.Contains("empuje", StringComparison.OrdinalIgnoreCase)
                    ? new HashSet<TrainingMuscleGroup> { TrainingMuscleGroup.Chest, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Triceps }
                    : title.Contains("tracción", StringComparison.OrdinalIgnoreCase) || title.Contains("traccion", StringComparison.OrdinalIgnoreCase)
                        ? new HashSet<TrainingMuscleGroup> { TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Forearms }
                        : null;

        // Pressing and rowing already count in full toward triceps, biceps and deltoids, so the
        // deficit-driven selection never asks for direct arm or lateral-delt work. In hypertrophy
        // plans, upper-body sessions get one extra accessory slot reserved for that isolation work
        // (the duration check below still protects the session limit).
        var isolationSlotsApply = request.Goal == TrainingGoal.Hypertrophy && allowedMuscles is not null &&
            (allowedMuscles.Contains(TrainingMuscleGroup.Biceps) || allowedMuscles.Contains(TrainingMuscleGroup.Triceps) ||
             allowedMuscles.Contains(TrainingMuscleGroup.Deltoids)) &&
            !allowedMuscles.Contains(TrainingMuscleGroup.Quadriceps);
        if (isolationSlotsApply)
            slots++;

        for (var slot = 0; slot < slots; slot++)
        {
            var currentEffectiveVolume = new Dictionary<TrainingMuscleGroup, decimal>();
            foreach (var exercise in previousSessions.Where(session => session.WeekNumber == week)
                         .SelectMany(session => session.Exercises).Concat(currentExercises))
            foreach (var muscle in TrainingExerciseStimulus.GetEffectiveMuscles(
                         exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups))
                currentEffectiveVolume[muscle] = currentEffectiveVolume.GetValueOrDefault(muscle) + exercise.Sets;

            var selectedThisWeek = previousSessions
                .Where(session => session.WeekNumber == week)
                .SelectMany(session => session.Exercises)
                .Concat(currentExercises)
                .Select(exercise => exercise.ExerciseId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hamstringsExplicitlyPrioritized = (request.MuscleVolumeTargets ?? Array.Empty<TrainingMuscleVolumeTarget>())
                .Any(preference => preference.MuscleGroup == TrainingMuscleGroup.Hamstrings &&
                    (preference.Priority > 0 || preference.TargetWeeklyPrimarySets is not null));
            var hasDirectHamstringAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var hasDirectCalfAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Calves) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var hasDirectLatAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Lats) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var selectedAccessoryPrimaryMuscles = currentExercises
                .SelectMany(exercise => catalogCandidates
                    .Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                        string.Equals(candidate.ExerciseId, exercise.ExerciseId, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(candidate => candidate.PrimaryMuscleGroups.Where(muscle =>
                    targets.ContainsKey(muscle) && (allowedMuscles is null || allowedMuscles.Contains(muscle)))))
                .ToHashSet();

            // Squat-pattern compounds commonly train both glutes and quadriceps. The volume
            // allocator may add several sets to those compounds later in the generation pass;
            // reserve a direct quad accessory when glutes are within a six-set planning buffer
            // of target, accounting for the compound sets the allocator may add later.
            var hasDirectQuadricepsAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var prioritizeQuadricepsAccessory =
                !hasDirectQuadricepsAccessory &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Glutes) + QuadAccessoryGluteTargetBufferSets >=
                    targets.GetValueOrDefault(TrainingMuscleGroup.Glutes) &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Quadriceps) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Quadriceps) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Quadriceps));
            // Calves are a product-priority group. Ensure at least one compatible direct
            // exposure before spending every short-session accessory slot on smaller muscles.
            var prioritizeCalfAccessory =
                !hasDirectCalfAccessory &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Calves) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Calves) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Calves));
            var prioritizeLatAccessory =
                !hasDirectLatAccessory &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Lats) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Lats) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Lats));

            // Remaining direct-isolation exposures needed this week (hypertrophy only).
            var directNeeds = new Dictionary<DirectIsolationGap, int>();
            if (isolationSlotsApply)
            {
                var directCounts = catalogCandidates
                    .Where(candidate => selectedThisWeek.Contains(candidate.ExerciseId))
                    .Select(GetDirectIsolationGap)
                    .Where(gap => gap != DirectIsolationGap.None)
                    .GroupBy(gap => gap)
                    .ToDictionary(group => group.Key, group => group.Count());
                foreach (var gap in new[] { DirectIsolationGap.LateralDeltoid, DirectIsolationGap.Biceps, DirectIsolationGap.Triceps })
                    directNeeds[gap] = Math.Max(0, DirectIsolationWeeklyExposures - directCounts.GetValueOrDefault(gap));
            }

            int DirectNeed(TrainingExerciseCandidate candidate) =>
                directNeeds.GetValueOrDefault(GetDirectIsolationGap(candidate));

            var candidates = catalogCandidates
                .Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory
                    && (HasExplicitCorePriority(request.MuscleVolumeTargets) || !IsDirectCoreExercise(candidate))
                    && (candidate.IsReviewed || candidate.IsOwnedByCurrentProfessional)
                    && IsAccessoryGoalCompatible(candidate, request.Goal)
                    && (int)candidate.MinimumExperience <= (int)request.Experience
                    && (equipment.Count == 0 || HasRequiredEquipment(candidate.RequiredEquipment, equipment))
                    && !excluded.Contains(candidate.ExerciseId)
                    && (allowedMuscles is null || TrainingExerciseStimulus.GetEffectiveMuscles(
                        candidate.ExerciseId, candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups)
                        .Any(allowedMuscles.Contains))
                    // By default, one direct hamstring accessory is enough because this group has
                    // lower allocation priority. An explicit professional target/priority can repeat it.
                    && !(hasDirectHamstringAccessory && !hamstringsExplicitlyPrioritized &&
                         candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings))
                    // Other accessory exercises may repeat on different training days. Only prevent
                    // duplicate prescription of the same accessory within this session.
                    && !currentExercises.Any(exercise =>
                        string.Equals(exercise.ExerciseId, candidate.ExerciseId, StringComparison.OrdinalIgnoreCase))
                    // Avoid spending multiple accessory slots on the same primary muscle in one
                    // session while another direct target group still has no accessory exposure.
                    && candidate.PrimaryMuscleGroups.Any(muscle =>
                        targets.ContainsKey(muscle) &&
                        (allowedMuscles is null || allowedMuscles.Contains(muscle)) &&
                        !selectedAccessoryPrimaryMuscles.Contains(muscle)))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = AccessoryDeficitScore(candidate, currentEffectiveVolume, targets, allowedMuscles, request.MuscleVolumeTargets)
                })
                .Where(item => item.Score > 0m || DirectNeed(item.Candidate) > 0)
                // Quadriceps receive first call on an accessory slot when glutes are already
                // at target and quads remain under target. Hamstring accessories compete by
                // their lower default target/score instead of reserving a slot unconditionally.
                .OrderByDescending(item =>
                    prioritizeQuadricepsAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps))
                .ThenByDescending(item =>
                    prioritizeCalfAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Calves))
                .ThenByDescending(item =>
                    prioritizeLatAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Lats))
                .ThenByDescending(item => DirectNeed(item.Candidate))
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.Candidate.SelectionPriority)
                .ThenBy(item => item.Candidate.ExerciseId, StringComparer.Ordinal)
                .ToArray();

            var selected = false;
            foreach (var item in candidates)
            {
                var candidate = item.Candidate;
                var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(
                    request.Goal, request.Experience, week, daysPerWeek, TrainingMovementPattern.Accessory);
                // The allocator only adds sets where a muscle is below target, and compounds already
                // cover arms/deltoids on paper, so a direct isolation exercise needs its own
                // starting dose instead of a single set.
                var directGap = isolationSlotsApply ? GetDirectIsolationGap(candidate) : DirectIsolationGap.None;
                if (directGap != DirectIsolationGap.None)
                    sets = directGap == DirectIsolationGap.LateralDeltoid ? DirectLateralDeltoidSets : DirectArmSets;
                var underTargetMuscles = TrainingExerciseStimulus.GetEffectiveMuscles(
                        candidate.ExerciseId, candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups)
                    .Where(muscle => currentEffectiveVolume.GetValueOrDefault(muscle) < targets.GetValueOrDefault(muscle))
                    .ToArray();
                var prescription = new TrainingExercisePrescription(
                    candidate.ExerciseId,
                    candidate.CatalogVersion,
                    candidate.Name,
                    currentExercises.Count + 1,
                    sets,
                    minReps,
                    maxReps,
                    rir,
                    rest,
                    progression,
                    $"Accesorio seleccionado para atender el déficit de estímulo efectivo de {string.Join(", ", underTargetMuscles)} sin sustituir los patrones básicos; metadatos {(candidate.IsReviewed ? "revisados" : "aportados por el profesional propietario")}.",
                    candidate.PrimaryMuscleGroups.ToArray(),
                    candidate.SecondaryMuscleGroups.ToArray());

                var durationSession = new TrainingSessionPrescription(
                    week,
                    day,
                    title,
                    WarmupAndTransitionsMinutes,
                    currentExercises.Append(prescription).Select(exercise =>
                        ReferenceEquals(exercise, prescription) ? exercise : exercise with { Sets = 1 }).ToArray());
                if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(durationSession) >
                    request.MaximumSessionDurationMinutes * 60m)
                    continue;

                currentExercises.Add(prescription);
                selected = true;
                break;
            }

            if (!selected)
                break;
        }
    }

    private enum DirectIsolationGap { None, LateralDeltoid, Biceps, Triceps }

    // Internal product conventions (not guideline figures): two direct exposures per week for
    // each of these groups, 3 sets for lateral raises and 2 for arm isolation.
    private const int DirectIsolationWeeklyExposures = 2;
    private const int DirectLateralDeltoidSets = 3;
    private const int DirectArmSets = 2;

    private static DirectIsolationGap GetDirectIsolationGap(TrainingExerciseCandidate candidate)
    {
        if (candidate.MovementPattern != TrainingMovementPattern.Accessory)
            return DirectIsolationGap.None;
        if (candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Deltoids) &&
            candidate.ExerciseId.Contains("lateral-raise", StringComparison.OrdinalIgnoreCase))
            return DirectIsolationGap.LateralDeltoid;
        if (candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Biceps))
            return DirectIsolationGap.Biceps;
        if (candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Triceps))
            return DirectIsolationGap.Triceps;
        return DirectIsolationGap.None;
    }

    private static readonly HashSet<TrainingMuscleGroup> UpperBodyAccessoryMuscles =
    [
        TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack,
        TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps,
        TrainingMuscleGroup.Forearms, TrainingMuscleGroup.Serratus, TrainingMuscleGroup.RotatorCuff
    ];

    private static readonly HashSet<TrainingMuscleGroup> LowerBodyAccessoryMuscles =
    [
        TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Hamstrings,
        TrainingMuscleGroup.Calves, TrainingMuscleGroup.Adductors, TrainingMuscleGroup.Abductors,
        TrainingMuscleGroup.HipFlexors, TrainingMuscleGroup.TibialisAnterior, TrainingMuscleGroup.ErectorSpinae
    ];

    // Higher score = bigger remaining weekly deficit across the muscles the accessory trains,
    // weighted by muscle importance (hamstrings count half by default; minor muscles 0.6).
    private static decimal AccessoryDeficitScore(
        TrainingExerciseCandidate candidate,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> currentEffectiveVolume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        IReadOnlySet<TrainingMuscleGroup>? allowedMuscles,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        var muscles = TrainingExerciseStimulus.GetEffectiveMuscles(
                candidate.ExerciseId, candidate.PrimaryMuscleGroups, candidate.SecondaryMuscleGroups)
            .Where(muscle => targets.ContainsKey(muscle) &&
                             (allowedMuscles is null || allowedMuscles.Contains(muscle)))
            .ToArray();
        if (muscles.Length == 0) return 0m;
        return muscles.Average(muscle =>
        {
            var deficit = Math.Max(0m, targets[muscle] - currentEffectiveVolume.GetValueOrDefault(muscle));
            var importance = muscle == TrainingMuscleGroup.Hamstrings
                ? 0.5m
                : muscle is TrainingMuscleGroup.Chest or TrainingMuscleGroup.Lats or
                    TrainingMuscleGroup.UpperBack or TrainingMuscleGroup.Deltoids or TrainingMuscleGroup.Biceps or
                    TrainingMuscleGroup.Triceps or TrainingMuscleGroup.Glutes or TrainingMuscleGroup.Quadriceps or
                    TrainingMuscleGroup.Calves ? 1m : 0.6m;
            return deficit * importance;
        });
    }

    // Weekly templates. Every template lists the movement patterns a session must cover; titles
    // are also used later to restrict accessory muscles (e.g. "superior", "Empuje"). Unsupported
    // split/day combinations return an empty list and are rejected earlier in ValidateRequest.
    private static IReadOnlyList<SessionTemplate> BuildSplit(int daysPerWeek, TrainingProgramSplit requestedSplit)
    {
        var fullBodyA = new SessionTemplate("Cuerpo completo A",
            [TrainingMovementPattern.Squat, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.Hinge, TrainingMovementPattern.Core]);
        // B templates reverse the order of coexisting large groups (push/pull, quads/posterior chain)
        // so the muscle that was trained second in A is trained fresh first in B. The squat stays
        // first in full-body sessions; only the upper-body pair is swapped.
        var fullBodyB = new SessionTemplate("Cuerpo completo B",
            [TrainingMovementPattern.Squat, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);
        var upperA = new SessionTemplate("Tren superior A",
            [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]);
        var lowerA = new SessionTemplate("Tren inferior A",
            [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);
        var upperB = new SessionTemplate("Tren superior B",
            [TrainingMovementPattern.VerticalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.Core]);
        var lowerB = new SessionTemplate("Tren inferior B",
            [TrainingMovementPattern.Hinge, TrainingMovementPattern.Squat, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);
        var push = new SessionTemplate("Empuje",
            [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.Core]);
        var pull = new SessionTemplate("Tracción",
            [TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]);
        var pushB = new SessionTemplate("Empuje B", [TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.Core]);
        var pullB = new SessionTemplate("Tracción B", [TrainingMovementPattern.VerticalPull, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.Core]);
        var legs = new SessionTemplate("Pierna",
            [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

        return requestedSplit switch
        {
            TrainingProgramSplit.FullBody when daysPerWeek == 2 => [fullBodyA, fullBodyB],
            TrainingProgramSplit.FullBody when daysPerWeek == 3 => [fullBodyA, fullBodyB, fullBodyA with { Title = "Cuerpo completo C" }],
            TrainingProgramSplit.FullBody when daysPerWeek == 4 => [fullBodyA, fullBodyB, fullBodyA with { Title = "Cuerpo completo C" }, fullBodyB with { Title = "Cuerpo completo D" }],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 5 => [push, pull, legs, pushB, pullB],
            TrainingProgramSplit.UpperLower when daysPerWeek == 2 => [upperA, lowerA],
            TrainingProgramSplit.UpperLower when daysPerWeek == 4 => [upperA, lowerA, upperB, lowerB],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 3 => [push, pull, legs],
            TrainingProgramSplit.PushPullLegs when daysPerWeek == 6 => [push, pull, legs, pushB, pullB, legs with { Title = "Pierna B", Patterns = lowerB.Patterns }],
            TrainingProgramSplit.Automatic => daysPerWeek switch
            {
                2 => [fullBodyA, fullBodyB],
                3 => [fullBodyA, fullBodyB, fullBodyA with { Title = "Cuerpo completo C" }],
                4 => [upperA, lowerA, upperB, lowerB],
                _ => []
            },
            _ => []
        };
    }

    /// <summary>
    /// Keep rep ranges and effort targets stable through the mesocycle. Weekly set distribution
    /// is handled by TrainingProgramVolumeAllocator. Deload planning is a separate, explicit
    /// mesocycle decision; this prescription method never imposes a fixed calendar deload.
    /// </summary>
    private static (int Sets, int MinReps, int MaxReps, decimal Rir, int RestSeconds, TrainingProgressionRule Progression)
        PrescriptionFor(TrainingGoal goal, TrainingExperience experience, int week, int daysPerWeek, TrainingMovementPattern pattern, bool isPrimaryLift = false)
    {
        var beginner = experience == TrainingExperience.Beginner;
        if (pattern is TrainingMovementPattern.Accessory or TrainingMovementPattern.Core or TrainingMovementPattern.SingleLeg)
            return (1, goal == TrainingGoal.Strength ? 8 : 10, 15,
                beginner ? 3m : 2m, pattern == TrainingMovementPattern.Core ? 60 : 90,
                TrainingProgressionRule.DoubleProgression);
        // Internal product heuristic (not an ACSM-prescribed figure): in strength plans for
        // non-beginners, the first compound of each session is the session's primary lift and is
        // trained with lower repetitions and longer rest. Beginners keep the 5–8 range to
        // consolidate technique; the other compounds and accessories keep their ranges.
        if (goal == TrainingGoal.Strength && isPrimaryLift && !beginner &&
            pattern is TrainingMovementPattern.Squat or TrainingMovementPattern.Hinge
                or TrainingMovementPattern.HorizontalPush or TrainingMovementPattern.VerticalPush
                or TrainingMovementPattern.HorizontalPull or TrainingMovementPattern.VerticalPull)
            return (2, 3, 6, 2m, 240, TrainingProgressionRule.DoubleProgression);
        // Give the main bilateral compound patterns a two-set starting dose. The allocator
        // preserves this baseline and adds volume only where the weekly targets and time allow.
        var baseline = goal switch
        {
            TrainingGoal.Strength => (Sets: 2, MinReps: 5, MaxReps: 8, Rir: beginner ? 3m : 2m, RestSeconds: 180),
            TrainingGoal.Hypertrophy => (Sets: 2, MinReps: 8, MaxReps: 12, Rir: beginner ? 3m : 2m, RestSeconds: 120),
            _ => (Sets: 2, MinReps: 8, MaxReps: 15, Rir: 3m, RestSeconds: 90)
        };
        return (baseline.Sets, baseline.MinReps, baseline.MaxReps, baseline.Rir,
            baseline.RestSeconds, TrainingProgressionRule.DoubleProgression);
    }

    private sealed record SessionTemplate(string Title, IReadOnlyList<TrainingMovementPattern> Patterns);
}

public enum TrainingProgramSplit
{
    Automatic,
    FullBody,
    UpperLower,
    PushPullLegs
}

public sealed record TrainingProgramGenerationRequest(
    TrainingGoal Goal,
    TrainingExperience Experience,
    DateOnly StartDate,
    int DurationWeeks,
    IReadOnlyList<DayOfWeek> TrainingDays,
    int MaximumSessionDurationMinutes,
    IReadOnlyList<string> AvailableEquipment,
    IReadOnlyList<string> ExcludedExerciseIds,
    IReadOnlyList<TrainingMuscleVolumeTarget>? MuscleVolumeTargets = null,
    bool IsDeload = false,
    TrainingProgramSplit Split = TrainingProgramSplit.Automatic,
    int? RotationIndex = null,
    IReadOnlyList<TrainingLoadReference>? LoadReferences = null,
    // History of the previous block of the same plan (filled by the server, never trusted for safety):
    // exercises it used, and whether this block directly continues a non-deload block.
    IReadOnlyList<string>? PreviousBlockExerciseIds = null,
    bool ContinuesPreviousBlock = false);

public sealed record TrainingExerciseCandidate(
    string ExerciseId,
    string CatalogVersion,
    string Name,
    TrainingMovementPattern MovementPattern,
    IReadOnlyList<string> RequiredEquipment,
    IReadOnlyList<TrainingGoal> SuitableGoals,
    TrainingExperience MinimumExperience,
    int SelectionPriority,
    bool IsReviewed,
    IReadOnlyList<TrainingMuscleGroup> PrimaryMuscleGroups,
    IReadOnlyList<TrainingMuscleGroup> SecondaryMuscleGroups,
    bool IsOwnedByCurrentProfessional = false);

public sealed record TrainingProgramGenerationResult(
    TrainingProgramPrescription? Prescription,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Prescription is not null && Errors.Count == 0;
}

public enum TrainingGoal
{
    GeneralFitness,
    Hypertrophy,
    Strength
}

public enum TrainingExperience
{
    Beginner,
    Intermediate,
    Advanced
}

public enum TrainingMovementPattern
{
    Squat,
    Hinge,
    HorizontalPush,
    VerticalPush,
    HorizontalPull,
    VerticalPull,
    SingleLeg,
    Core,
    Accessory
}
