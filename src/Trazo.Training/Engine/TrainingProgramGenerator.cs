namespace Trazo.Training;

/// <summary>
/// First deterministic draft generator. It only selects from explicitly supplied, reviewed catalog metadata.
/// It does not infer exercise properties from names or automatically publish/apply the generated plan.
///
/// Flow of <see cref="Generate"/> (one call produces ONE block/mesocycle of 1–52 weeks):
///  1. Validate the request and the catalog (structural errors are returned, never thrown).
///  2. Pick the split (a week, or a continuous rotation of sessions) (<see cref="TrainingSessionRotation"/>) and, per session, one exercise per movement
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
    public const string EngineVersion = "0.18.0";
    // The main lift of a strength session is raised above three sets only while the session's compound lifts stay
    // within this share of the time offered.
    private const decimal StrengthMainLiftTimeShare = 0.85m;

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

        var unit = GenerationUnit.For(request);
        var excluded = new HashSet<string>(request.ExcludedExerciseIds, StringComparer.OrdinalIgnoreCase);
        var equipment = new HashSet<string>(
            request.AvailableEquipment
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeEquipmentToken),
            StringComparer.OrdinalIgnoreCase);
        var selectionWarnings = new List<string>();
        int Score(TrainingExerciseCandidate candidate) =>
            TrainingExerciseSelection.Score(candidate, request.Goal, request.Experience);
        // Candidates of a pattern that fit the profile, best first. Ties are broken with the plan seed, never by id.
        var compatibleByPattern = new Dictionary<TrainingMovementPattern, TrainingExerciseCandidate[]>();
        TrainingExerciseCandidate[] Compatible(TrainingMovementPattern pattern)
        {
            if (compatibleByPattern.TryGetValue(pattern, out var cached)) return cached;
            var usable = catalog
                .Where(x => (x.IsReviewed || x.IsOwnedByCurrentProfessional)
                    && x.MovementPattern == pattern
                    && (int)x.MinimumExperience <= (int)request.Experience
                    && (equipment.Count == 0 || HasRequiredEquipment(x.RequiredEquipment, equipment))
                    && !excluded.Contains(x.ExerciseId))
                .ToArray();
            var fitting = usable.Where(x => x.SuitableGoals.Contains(request.Goal)).ToArray();
            // With little material nothing of a pattern may be marked for the goal (a bodyweight squat in a strength plan).
            // The pattern is then trained with what there is, and the plan says so, instead of not being generated at all.
            if (fitting.Length == 0 && usable.Length > 0)
            {
                fitting = usable;
                selectionWarnings.Add($"Ningún ejercicio de patrón {pattern} pensado para este objetivo es compatible con el material y las limitaciones indicadas; se usa el mejor disponible, que puede no admitir mucha carga.");
            }
            // The hinge is the only compound slot that trains the hamstrings. Whenever the profile has a hinge
            // with the hamstrings as a primary muscle (a deadlift or a Romanian deadlift), the glute-only and
            // upper-back variants (bridge, hip thrust, rack pull) are left out of the slot.
            if (pattern == TrainingMovementPattern.Hinge &&
                fitting.Any(x => x.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings)))
                fitting = fitting.Where(x => x.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings)).ToArray();
            return compatibleByPattern[pattern] = fitting
                .OrderByDescending(Score)
                .ThenBy(x => TrainingExerciseSelection.TieBreak(request.SelectionSeed, x.ExerciseId))
                .ThenBy(x => x.ExerciseId, StringComparer.Ordinal)
                .ToArray();
        }

        var sessionPatterns = PlanSessionPatterns(unit.Templates, request, Compatible);
        var sessions = new List<TrainingSessionPrescription>();
        // The sessions of ONE unit (a week of a calendar plan, one pass through the cycle of a sequence plan) are chosen here. Their
        // selection does not depend on the week: it is stable across weeks to support measurable progression, and variety is managed
        // within the unit, not across the mesocycle. The unit is allocated once and then laid out over the weeks of the block.
        const int week = 1;
        var selectedThisWeek = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var dayIndex = 0; dayIndex < unit.Templates.Count; dayIndex++)
        {
            var sessionTemplate = unit.Templates[dayIndex];
            var requiredPatterns = sessionPatterns[dayIndex];
            var prescriptions = new List<TrainingExercisePrescription>();
            for (var patternIndex = 0; patternIndex < requiredPatterns.Count; patternIndex++)
            {
                var pattern = requiredPatterns[patternIndex];
                var compatibleCandidates = Compatible(pattern);

                if (pattern == TrainingMovementPattern.Core)
                {
                    // Core is optional work: a profile without a compatible core exercise still gets its plan.
                    var direct = compatibleCandidates.Where(IsDirectCoreExercise).ToArray();
                    if (direct.Length > 0) compatibleCandidates = direct;
                    if (compatibleCandidates.Length == 0)
                        continue;
                }

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

                    compatibleCandidates = Compatible(TrainingMovementPattern.HorizontalPull);
                }

                // The same idea for the two patterns a limitation or a small home gym can leave empty: overhead
                // pressing (shoulder) and single-leg work (knee). The session keeps its sibling pattern when it
                // has one (horizontal press, hinge) and receives it as a substitute when it does not. The
                // omission is reported in the plan's warnings; squat, hinge, horizontal press and row are never
                // dropped, so a profile without them still fails with the explanation below.
                if (compatibleCandidates.Length == 0 &&
                    pattern is TrainingMovementPattern.VerticalPush or TrainingMovementPattern.SingleLeg)
                {
                    var sibling = pattern == TrainingMovementPattern.VerticalPush
                        ? TrainingMovementPattern.HorizontalPush
                        : TrainingMovementPattern.Hinge;
                    var substitutes = Compatible(sibling);
                    if (requiredPatterns.Contains(sibling) || substitutes.Length == 0)
                    {
                        if (requiredPatterns.Contains(sibling))
                        {
                            selectionWarnings.Add($"'{sessionTemplate.Title}': ningún ejercicio de patrón {pattern} es compatible con el material, el nivel y las exclusiones o limitaciones indicadas; la sesión lo omite y conserva el patrón {sibling}.");
                            continue;
                        }
                    }
                    else
                    {
                        selectionWarnings.Add($"'{sessionTemplate.Title}': ningún ejercicio de patrón {pattern} es compatible con el material, el nivel y las exclusiones o limitaciones indicadas; se sustituye por un ejercicio de patrón {sibling}.");
                        compatibleCandidates = substitutes;
                    }
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

                if (compatibleCandidates.Length == 0)
                    return Failure(BuildCandidateDiagnostic(pattern, request, catalog, equipment, excluded));

                // The main lift of a strength session must accept high loads: a barbell or a guided machine,
                // never a goblet squat, a dumbbell squat or a push-up, whenever the profile has one.
                var isFirstExercise = prescriptions.Count == 0;
                var strength = request.Goal == TrainingGoal.Strength;
                if (strength && isFirstExercise)
                {
                    var heavy = compatibleCandidates.Where(TrainingExerciseSelection.IsHeavy).ToArray();
                    if (heavy.Length > 0) compatibleCandidates = heavy;
                }

                // Strength progresses by adding load to the same lifts, so its selection depends only on the
                // profile and the plan seed: every block of the plan trains the same exercises on the same days.
                // The other goals rotate among the few best exercises from block to block.
                var ordered = strength
                    ? compatibleCandidates
                    : RotateCuratedPool(compatibleCandidates, Score, request.RotationIndex ?? 0, request.PreviousBlockExerciseIds, request.IsDeload);

                // Prefer the exercise used least elsewhere in this week's plan, but only among candidates about
                // as good as the best one: repeating a good exercise on two days is better than reaching for a
                // clearly worse one (a regression far below the user's level) just to add variety.
                // Never penalize an exercise for being selected in previous weeks: continuity
                // across a mesocycle supports progression and performance tracking.
                var bestScore = compatibleCandidates.Max(Score);
                // The main lift of a strength session is always the best heavy lift of its pattern.
                var candidate = strength && isFirstExercise
                    ? ordered[0]
                    : ordered
                        .Where(x => bestScore - Score(x) <= PoolPriorityWindow)
                        .MinBy(x => selectedThisWeek.GetValueOrDefault(x.ExerciseId))!;

                selectedThisWeek[candidate.ExerciseId] = selectedThisWeek.GetValueOrDefault(candidate.ExerciseId) + 1;

                var heavyPrimaryLift = isFirstExercise && TrainingExerciseSelection.IsHeavy(candidate);
                var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(
                    request.Goal, request.Experience, week, unit.SessionsPerPeriod, pattern,
                    isPrimaryLift: isFirstExercise, isHeavyLift: heavyPrimaryLift);
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
                    candidate.SecondaryMuscleGroups.ToArray(),
                    EffectiveSecondaryMuscleGroups: candidate.EffectiveSecondaryMuscleGroups ?? []));
            }

            // The main lift of a strength session gets its dose from the lift, not from the weekly muscle
            // accounting: three sets for beginners, four for intermediates and five for advanced lifters, as
            // long as the session keeps a margin inside the time offered. It is set before the accessories are
            // chosen so they see what the session already trains.
            if (request.Goal == TrainingGoal.Strength && prescriptions.Count > 0)
            {
                var mainLiftSets = request.Experience switch
                {
                    TrainingExperience.Beginner => 3,
                    TrainingExperience.Intermediate => 4,
                    _ => 5
                };
                for (var mainSets = mainLiftSets; mainSets > prescriptions[0].Sets; mainSets--)
                {
                    var raised = prescriptions.Select((exercise, index) => index == 0 ? exercise with { Sets = mainSets } : exercise).ToArray();
                    var raisedSession = new TrainingSessionPrescription(
                        week, unit.Days[dayIndex], sessionTemplate.Title, WarmupAndTransitionsMinutes, raised);
                    if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(raisedSession) >
                        request.MaximumSessionDurationMinutes * 60m * StrengthMainLiftTimeShare)
                        continue;
                    prescriptions[0] = raised[0];
                    break;
                }
            }

            AddOptionalAccessoryExercises(
                request, catalog, equipment, excluded, sessions,
                prescriptions, week, unit.Days[dayIndex], sessionTemplate.Title, unit.SessionsPerPeriod, unit.WeeksPerUnit);

            // Final duration is calculated after weekly sets have been allocated.
            sessions.Add(new TrainingSessionPrescription(
                week,
                unit.Days[dayIndex],
                sessionTemplate.Title,
                WarmupAndTransitionsMinutes,
                prescriptions));
        }

        // Accessory and core exercises: the allocator gives weekly volume to the compound lifts before them.
        var accessoryIds = catalog
            .Where(x => x.MovementPattern is TrainingMovementPattern.Accessory or TrainingMovementPattern.Core)
            .Select(x => x.ExerciseId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, request.Goal, request.Experience, request.MuscleVolumeTargets,
            request.MaximumSessionDurationMinutes, accessoryIds, unit.WeeksPerUnit, unit.SessionsPerPeriod);
        if (!allocation.Succeeded)
            return new(null, allocation.Errors);
        var balanced = TrainingProgramBalancer.Balance(
            allocation.Sessions, catalog, request.Goal, request.MaximumSessionDurationMinutes);
        var allocatedSessions = unit.LayOut(balanced, request.DurationWeeks);

        // Allocate volume before reordering. The allocator resolves exact ties by the current
        // exercise order; sorting earlier would silently move sets between exercises even though
        // the orderer itself only changes Order. Apply presentation/fatigue-aware ordering only
        // after weekly set allocation is complete.
        var orderedAllocatedSessions = allocatedSessions.Select(session => session with
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
            EngineVersion,
            request.Goal.ToString(),
            request.StartDate,
            request.DurationWeeks,
            prescribedSessions);
        // A sequence plan has no weekly groups: its report describes one pass through the rotation, normalised to seven days.
        var qualityReport = unit.IsSequence
            ? TrainingProgramQualityAnalyzer.Analyze(plan with { DurationWeeks = 1, Sessions = allocation.Sessions }, unit.WeeksPerUnit, rotation: true)
            : TrainingProgramQualityAnalyzer.Analyze(plan);
        var oneSetExposures = allocatedSessions.SelectMany(session => session.Exercises)
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
                Warnings = qualityReport.Warnings.Concat(selectionWarnings).Concat(unit.Warnings(allocation.Warnings, request.DurationWeeks)).Concat(deloadWarnings).Distinct(StringComparer.Ordinal).ToArray()
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
    /// guideline figure; general fitness keeps the legacy scheme). Effort rises across the block (RIR offset +1 in week 1, 0 in the middle
    /// weeks and -1 in the last one; blocks of five weeks or more step down in halves). The last week reaches RIR 1 in hypertrophy;
    /// beginners stop at RIR 2, and so do the compound lifts of strength. In hypertrophy blocks of three or more weeks volume rises too:
    /// the first week (the first two in blocks of four or more) trims one set from exercises with three or more sets, the middle weeks
    /// keep the allocated dose and the last week adds at most one set per muscle, never above the muscle's weekly ceiling, the
    /// per-exercise limit or the session duration. It is never a deload. A block that continues a previous one has no easier start:
    /// no extra reserve and no set trimming. A beginner coming back from a deload or a preparation starts at the usual reserve.
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
        // Volume ramp (trimmed first weeks, extra set in the last one) is hypertrophy-only; strength
        // progresses through load, so it only gets the effort ramp and the legacy week-1 trim.
        var rampVolume = request.Goal == TrainingGoal.Hypertrophy;
        var continues = request.ContinuesPreviousBlock;

        var result = new List<TrainingSessionPrescription>();
        foreach (var weekGroup in sessions.GroupBy(x => x.WeekNumber).OrderBy(g => g.Key))
        {
            var week = weekGroup.Key;
            var offset = HypertrophyRirOffset(week, weeks, continues);
            // Only a first block starts further from failure for a beginner: nobody goes back to that after a lighter block.
            if (request.ReturnsFromLighterBlock && request.Experience == TrainingExperience.Beginner) offset = Math.Min(offset, 0m);
            var trimSet = !continues && (rampVolume
                ? (weeks >= 4 && week <= 2) || (weeks == 3 && week == 1)
                : weeks >= 4 && week == 1);
            var weekSessions = weekGroup.OrderBy(x => x.Day).Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise =>
                {
                    var isIsolation = patternById.TryGetValue(exercise.ExerciseId, out var pattern) &&
                        (pattern == TrainingMovementPattern.Accessory || pattern == TrainingMovementPattern.Core);
                    var floor = request.Experience != TrainingExperience.Beginner &&
                        (isIsolation || request.Goal == TrainingGoal.Hypertrophy) ? 1m : 2m;
                    return exercise with
                    {
                        // Strength keeps at least three sets: the introductory week only trims lifts with four or more.
                        Sets = trimSet && exercise.Sets >= (rampVolume ? 3 : 4) ? exercise.Sets - 1 : exercise.Sets,
                        TargetRepetitionsInReserve = exercise.TargetRepetitionsInReserve is { } rir
                            ? ApplyRirOffset(rir, offset, floor)
                            : null
                    };
                }).ToArray()
            }).ToArray();

            if (rampVolume && weeks >= 3 && week == weeks)
                AddPeakWeekSets(weekSessions, ceilings, maxSets, request.MaximumSessionDurationMinutes, patternById);

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
        else if (week == 1) offset = 1m;
        // Whole repetitions in the blocks of up to four weeks: nobody can judge half a repetition in reserve.
        else if (weeks <= 4) offset = 0m;
        else offset = Math.Round(0.5m * (1m - (week - 2m) / (weeks - 3m)) * 2m, MidpointRounding.ToEven) / 2m;

        // A continuing block is already adapted: no introductory reserve, same final push.
        return continues ? Math.Min(offset, 0m) : offset;
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
        int maximumSessionDurationMinutes,
        IReadOnlyDictionary<string, TrainingMovementPattern> patternById)
    {
        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(weekSessions)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var boosted = new HashSet<TrainingMuscleGroup>();
        for (var sessionIndex = 0; sessionIndex < weekSessions.Length; sessionIndex++)
        {
            foreach (var exercise in weekSessions[sessionIndex].Exercises.OrderBy(x => x.Order).ToArray())
            {
                if (exercise.Sets >= maxSets) continue;
                var tracked = exercise.StimulatedMuscles()
                    .Where(ceilings.ContainsKey)
                    .ToArray();
                if (tracked.Length == 0 || tracked.Any(boosted.Contains)) continue;
                if (tracked.Any(muscle => volume.GetValueOrDefault(muscle) + 1m > ceilings[muscle])) continue;
                if (TrainingProgramVolumeAllocator.ExceedsSessionDose(weekSessions[sessionIndex], exercise)) continue;

                var session = weekSessions[sessionIndex];
                var candidateExercises = session.Exercises
                    .Select(x => x.Order == exercise.Order ? x with { Sets = x.Sets + 1 } : x)
                    .ToArray();
                if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(session with { Exercises = candidateExercises }) >
                    maximumSessionDurationMinutes * 60m)
                    continue;

                var updated = session with { Exercises = candidateExercises };
                if (TrainingProgramBalancer.Worsens(weekSessions, sessionIndex, updated, patternById)) continue;
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
                ? 0m
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

    private static readonly HashSet<TrainingMuscleGroup> SmallIsolationMuscles =
    [
        TrainingMuscleGroup.Calves, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Forearms, TrainingMuscleGroup.Abdominals,
        TrainingMuscleGroup.Obliques
    ];

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
        IReadOnlyList<TrainingExerciseCandidate> orderedByPriority, Func<TrainingExerciseCandidate, int> score,
        int rotationIndex, IReadOnlyList<string>? previousBlockExerciseIds = null, bool isDeload = false)
    {
        if (orderedByPriority.Count < 2) return orderedByPriority;
        var best = score(orderedByPriority[0]);
        var withinWindow = orderedByPriority.Where(candidate => best - score(candidate) <= PoolPriorityWindow).ToArray();
        // The pool takes the best exercise of each implement family first (one barbell lift, one machine, one free
        // weight...), so several near-identical machines cannot fill it and leave the barbell lift out; the remaining
        // places go to the next best candidates.
        var pool = new List<TrainingExerciseCandidate>();
        var families = new HashSet<TrainingImplementClass>();
        foreach (var candidate in withinWindow)
            if (pool.Count < RotationPoolSize && families.Add(TrainingExerciseSelection.ImplementClass(candidate)))
                pool.Add(candidate);
        foreach (var candidate in withinWindow)
            if (pool.Count < RotationPoolSize && !pool.Contains(candidate))
                pool.Add(candidate);
        var rest = orderedByPriority.Where(candidate => !pool.Contains(candidate)).ToArray();
        if (previousBlockExerciseIds is { Count: > 0 })
        {
            // History-aware rotation: what the previous block really used decides, not the block number
            // (a one-week deload would otherwise shift the rotation). A training block moves to an equally
            // suitable exercise it did not use; a deload keeps the previous block's exercises.
            var used = previousBlockExerciseIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // The compound lifts of the previous block stay, so their loads go on progressing; variety comes from the
            // accessories (see AddOptionalAccessoryExercises).
            var ordered = pool.OrderBy(candidate => used.Contains(candidate.ExerciseId) ? 0 : 1).ToList();
            var keptOutside = rest.Where(candidate => used.Contains(candidate.ExerciseId) && best - score(candidate) <= PoolPriorityWindow).ToArray();
            return ordered.Where(c => used.Contains(c.ExerciseId)).Concat(keptOutside)
                .Concat(ordered.Where(c => !used.Contains(c.ExerciseId))).Concat(rest.Except(keptOutside)).ToArray();
        }
        var offset = Math.Max(0, rotationIndex) % pool.Count;
        return pool.Skip(offset).Concat(pool.Take(offset)).Concat(rest).ToArray();
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
                var increment = exercise.SmallestLoadIncrementKg ?? TrainingLoadEstimator.IncrementFor(reference.WeightKg);
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

    // Order in which a strength session looks for its main lift. A vertical pull is never the main lift: a
    // pulldown trained at 3-6 repetitions is not what a strength plan is built around.
    private static readonly TrainingMovementPattern[] StrengthMainLiftOrder =
    [
        TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.HorizontalPush,
        TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPull
    ];

    /// <summary>
    /// The movement patterns each session of the week really trains, in order.
    /// Core: no direct core work by default, in any goal. The squat, hinge, presses and rows (free weights, cables,
    /// anything the transversus has to stabilize) already train it, and spending time on it is wasted; it is added to
    /// every session only when the professional prioritizes the abdominal wall.
    /// Strength: the session opens with a main lift that can be loaded heavily, and the sessions of the week open
    /// with different patterns when they can (squat, press, deadlift) instead of three squat-first days.
    /// </summary>
    private static IReadOnlyList<TrainingMovementPattern>[] PlanSessionPatterns(
        IReadOnlyList<SessionTemplate> split,
        TrainingProgramGenerationRequest request,
        Func<TrainingMovementPattern, TrainingExerciseCandidate[]> compatible)
    {
        var everySessionHasCore = HasExplicitCorePriority(request.MuscleVolumeTargets);
        var openedWith = new HashSet<TrainingMovementPattern>();
        var result = new IReadOnlyList<TrainingMovementPattern>[split.Count];
        for (var index = 0; index < split.Count; index++)
        {
            var patterns = split[index].Patterns
                .Where(pattern => pattern != TrainingMovementPattern.Core || everySessionHasCore)
                .ToList();
            if (request.Goal == TrainingGoal.Strength && patterns.Count > 0)
            {
                var mainLifts = StrengthMainLiftOrder
                    .Where(pattern => patterns.Contains(pattern) && compatible(pattern).Any(TrainingExerciseSelection.IsHeavy))
                    .ToArray();
                if (mainLifts.Length > 0)
                {
                    var fresh = mainLifts.Where(pattern => !openedWith.Contains(pattern)).ToArray();
                    var mainLift = fresh.Length > 0
                        ? fresh[0]
                        : mainLifts.Contains(patterns[0]) ? patterns[0] : mainLifts[0];
                    openedWith.Add(mainLift);
                    patterns.Remove(mainLift);
                    patterns.Insert(0, mainLift);
                }
            }
            result[index] = patterns;
        }
        return result;
    }

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

    // Every required token must be explicitly available. Machine types are never interchangeable:
    // a leg press, Smith machine and leg extension machine are different pieces of equipment.
    internal static bool HasRequiredEquipment(
        IReadOnlyList<string> requiredEquipment,
        IReadOnlySet<string> availableEquipment)
    {
        if (availableEquipment.Count == 0) return true;
        return requiredEquipment.All(required => availableEquipment.Contains(NormalizeEquipmentToken(required)));
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
        if (request.Sequence is { } sequence)
        {
            // A sequence plan has no training days: its split is laid over a continuous rotation of sessions.
            if (sequence.SessionsPerPeriod is < TrainingSessionRotation.MinimumSessionsPerPeriod or > TrainingSessionRotation.MaximumSessionsPerPeriod)
                errors.Add("Indica entre 2 y 6 sesiones por ciclo.");
            if (sequence.StartPosition < 0)
                errors.Add("La posición inicial de la rotación no puede ser negativa.");
            if (!Enum.IsDefined(request.Split)) errors.Add("La distribución semanal seleccionada no es válida.");
            else if (!TrainingSessionRotation.IsSupportedSequence(request.Split, sequence.SessionsPerPeriod))
                errors.Add(request.Split == TrainingProgramSplit.PushPullLegs
                    ? "Push/Pull/Legs admite de 3 a 6 sesiones por ciclo; cambia el número de sesiones o elige otra distribución."
                    : "Esta distribución admite de 2 a 6 sesiones por ciclo.");
        }
        else
        {
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
            else if (request.TrainingDays is not null && !TrainingSessionRotation.IsSupportedWeekly(request.Split, request.TrainingDays.Count))
                errors.Add(request.Split switch
                {
                    TrainingProgramSplit.FullBody => "Cuerpo completo admite de 2 a 4 días; cambia la disponibilidad o elige otra distribución.",
                    TrainingProgramSplit.UpperLower => "Torso/Pierna admite 2 o 4 días; cambia la disponibilidad o elige otra distribución.",
                    TrainingProgramSplit.PushPullLegs => "Push/Pull/Legs admite 3, 5 o 6 días; cambia la disponibilidad o elige otra distribución.",
                    TrainingProgramSplit.Hybrid => "La distribución híbrida necesita 5 días; cambia la disponibilidad o elige otra distribución.",
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
                if (exercise.RequiredEquipment.Any(value => !string.IsNullOrWhiteSpace(value) && NormalizeEquipmentToken(value).Equals("machine", StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' debe especificar el tipo concreto de máquina, no el requisito genérico 'machine'.");
                if (exercise.SuitableGoals.Any(goal => !Enum.IsDefined(goal)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un objetivo compatible no válido.");
                if (exercise.SuitableGoals.Distinct().Count() != exercise.SuitableGoals.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite objetivos compatibles.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Adds only the optional isolation/accessory layer after the session's required movement patterns have been selected.
    /// The candidate pool is constrained by the split (upper/lower/push/pull), equipment, exclusions and remaining session time;
    /// within that pool the engine chooses the muscles with the largest remaining weekly deficit. This stage must not be used
    /// to compensate for a missing required compound pattern: those failures are handled earlier and reported explicitly.
    /// </summary>
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
        int daysPerWeek,
        decimal weeksPerUnit)
    {
        // The deficit of a unit is judged against its share of the weekly targets (the whole week for a calendar plan).
        var targets = TrainingProgramVolumeAllocator.GetWeeklyTargets(
                request.Goal, request.Experience, request.MuscleVolumeTargets,
                daysPerWeek, request.MaximumSessionDurationMinutes)
            .ToDictionary(pair => pair.Key, pair => pair.Value * weeksPerUnit);
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

        // An emphasized muscle gets one more slot of its own after the regular ones: with a fixed number of exercises, each
        // stopped at its set ceiling, a higher weekly target could otherwise add nothing at all. It is used only while the
        // muscle is under that target and the session has time for it.
        var emphasized = (request.MuscleVolumeTargets ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Where(preference => preference.Priority > 0 && !preference.Maintenance && !preference.Learned)
            .Select(preference => preference.MuscleGroup)
            .ToHashSet();
        // A full-body session has no upper-body slot, and its first calls (hamstrings, quadriceps, lats, chest) usually take
        // the regular ones, so the arms were left to presses and rows. One more slot, only for direct biceps or triceps work,
        // comes last and only where the session has time. Short sessions keep their time for the compound lifts.
        var armSlot = request.Goal == TrainingGoal.Hypertrophy && allowedMuscles is null && isFullBody &&
            request.MaximumSessionDurationMinutes > ShortSessionMinutes;
        var slotKinds = Enumerable.Repeat(AccessorySlot.Regular, slots).ToList();
        if (emphasized.Count > 0) slotKinds.Add(AccessorySlot.Emphasis);
        if (armSlot) slotKinds.Add(AccessorySlot.Arms);
        // A new training block renews accessories; a deload keeps the previous block's exercises.
        var renewed = !request.IsDeload
            ? (request.PreviousBlockExerciseIds ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

        var regularExhausted = false;
        foreach (var slotKind in slotKinds)
        {
            if (slotKind == AccessorySlot.Regular && regularExhausted) continue;
            var currentEffectiveVolume = new Dictionary<TrainingMuscleGroup, decimal>();
            foreach (var exercise in previousSessions.Where(session => session.WeekNumber == week)
                         .SelectMany(session => session.Exercises).Concat(currentExercises))
            foreach (var muscle in exercise.StimulatedMuscles())
                currentEffectiveVolume[muscle] = currentEffectiveVolume.GetValueOrDefault(muscle) + exercise.Sets;

            var selectedThisWeek = previousSessions
                .Where(session => session.WeekNumber == week)
                .SelectMany(session => session.Exercises)
                .Concat(currentExercises)
                .Select(exercise => exercise.ExerciseId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hamstringsExplicitlyPrioritized = (request.MuscleVolumeTargets ?? Array.Empty<TrainingMuscleVolumeTarget>())
                .Any(preference => preference.MuscleGroup == TrainingMuscleGroup.Hamstrings && !preference.Maintenance && !preference.Learned &&
                    (preference.Priority > 0 || preference.TargetWeeklyPrimarySets is not null));
            // Direct hamstring accessories (leg curls) already placed this week, counted per session.
            var directHamstringAccessoryIds = catalogCandidates
                .Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                    candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings))
                .Select(candidate => candidate.ExerciseId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directHamstringAccessories = previousSessions
                .Where(session => session.WeekNumber == week)
                .SelectMany(session => session.Exercises)
                .Concat(currentExercises)
                .Count(exercise => directHamstringAccessoryIds.Contains(exercise.ExerciseId));
            var hasDirectHamstringAccessory = directHamstringAccessories >= MaximumWeeklyHamstringAccessories;
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
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Glutes) + QuadAccessoryGluteTargetBufferSets * weeksPerUnit >=
                    targets.GetValueOrDefault(TrainingMuscleGroup.Glutes) &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Quadriceps) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Quadriceps) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Quadriceps));
            // Hamstrings only receive work from the hinge, so the first accessory slot of the week that may train
            // them goes to a leg curl while they are under target. Calves no longer reserve a slot: they compete
            // by deficit with a reduced weight, behind hamstrings, chest and back.
            var prioritizeHamstringAccessory =
                directHamstringAccessories == 0 &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Hamstrings) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Hamstrings) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Hamstrings));
            // In a hypertrophy plan with sessions longer than 45 minutes the calves get one direct exercise a
            // week, after hamstrings, quadriceps, lats and the direct arm and deltoid work have had their slot.
            // Everywhere else they compete by deficit with a reduced weight.
            var hasDirectCalfAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Calves) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var prioritizeCalfAccessory =
                request.Goal == TrainingGoal.Hypertrophy && !hasDirectCalfAccessory &&
                targets.ContainsKey(TrainingMuscleGroup.Calves) &&
                request.MaximumSessionDurationMinutes > 45 &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Calves));
            var prioritizeLatAccessory =
                !hasDirectLatAccessory &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Lats) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Lats) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Lats));
            // The chest gets the same first call as the lats. Without it the back had a reserved direct exercise and the chest did not, and
            // upper sessions ended with the chest at the bottom of its range (a press is limited to five sets) and the back above it.
            var hasDirectChestAccessory = catalogCandidates.Any(candidate =>
                candidate.MovementPattern == TrainingMovementPattern.Accessory &&
                candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Chest) &&
                selectedThisWeek.Contains(candidate.ExerciseId));
            var prioritizeChestAccessory =
                !hasDirectChestAccessory &&
                currentEffectiveVolume.GetValueOrDefault(TrainingMuscleGroup.Chest) <
                    targets.GetValueOrDefault(TrainingMuscleGroup.Chest) &&
                (allowedMuscles is null || allowedMuscles.Contains(TrainingMuscleGroup.Chest));

            // Remaining direct-isolation exposures needed this week (hypertrophy only).
            var directNeeds = new Dictionary<DirectIsolationGap, int>();
            // Outside its own slot a full body keeps choosing by deficit: the arms never displace a first call there.
            if (isolationSlotsApply || slotKind == AccessorySlot.Arms)
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
                    && (allowedMuscles is null || candidate.StimulatedMuscles()
                        .Any(allowedMuscles.Contains))
                    // By default, two direct hamstring accessories a week are enough.
                    // An explicit professional target/priority can add more.
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
                .Where(candidate => slotKind != AccessorySlot.Emphasis || candidate.PrimaryMuscleGroups.Any(muscle => emphasized.Contains(muscle) &&
                    currentEffectiveVolume.GetValueOrDefault(muscle) < targets.GetValueOrDefault(muscle)))
                .Where(candidate => slotKind != AccessorySlot.Arms ||
                    (GetDirectIsolationGap(candidate) is DirectIsolationGap.Biceps or DirectIsolationGap.Triceps && DirectNeed(candidate) > 0))
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
                    prioritizeHamstringAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings))
                .ThenByDescending(item =>
                    prioritizeQuadricepsAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps))
                .ThenByDescending(item =>
                    prioritizeLatAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Lats))
                .ThenByDescending(item =>
                    prioritizeChestAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Chest))
                .ThenByDescending(item => DirectNeed(item.Candidate))
                // On equal need the arm slot goes to the biceps: presses leave the triceps ahead once the sets are allocated.
                .ThenByDescending(item => slotKind == AccessorySlot.Arms &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Biceps))
                .ThenByDescending(item =>
                    prioritizeCalfAccessory &&
                    item.Candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Calves))
                .ThenByDescending(item => item.Score)
                // A new training block renews accessories: one the previous block used gives
                // way to an equally useful one that is about as well curated (the same window as the other rotations).
                .ThenByDescending(item => item.Candidate.SelectionPriority -
                    (renewed.Contains(item.Candidate.ExerciseId) ? PoolPriorityWindow + 1 : 0))
                // Between equally curated accessories, the one that can be loaded (a machine before a bodyweight drill).
                .ThenByDescending(item => (int)TrainingExerciseSelection.LoadTier(item.Candidate))
                .ThenBy(item => TrainingExerciseSelection.TieBreak(request.SelectionSeed, item.Candidate.ExerciseId))
                .ThenBy(item => item.Candidate.ExerciseId, StringComparer.Ordinal)
                .ToArray();

            var selected = false;
            foreach (var item in candidates)
            {
                var candidate = item.Candidate;
                var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(
                    request.Goal, request.Experience, week, daysPerWeek, TrainingMovementPattern.Accessory,
                    isSmallIsolation: candidate.PrimaryMuscleGroups.All(SmallIsolationMuscles.Contains));
                // The allocator only adds sets where a muscle is below target, and compounds already
                // cover arms/deltoids on paper, so a direct isolation exercise needs its own
                // starting dose instead of a single set.
                var directGap = isolationSlotsApply ? GetDirectIsolationGap(candidate) : DirectIsolationGap.None;
                if (directGap != DirectIsolationGap.None)
                    sets = directGap == DirectIsolationGap.LateralDeltoid ? DirectLateralDeltoidSets : DirectArmSets;
                var underTargetMuscles = candidate.StimulatedMuscles()
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
                    candidate.SecondaryMuscleGroups.ToArray(),
                    EffectiveSecondaryMuscleGroups: candidate.EffectiveSecondaryMuscleGroups ?? []);

                var durationSession = new TrainingSessionPrescription(
                    week,
                    day,
                    title,
                    WarmupAndTransitionsMinutes,
                    currentExercises.Append(prescription).ToArray());
                if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(durationSession) >
                    request.MaximumSessionDurationMinutes * 60m)
                    continue;

                currentExercises.Add(prescription);
                selected = true;
                break;
            }

            // When the regular slots run out of candidates the emphasis and arm slots still have their turn.
            if (!selected && slotKind == AccessorySlot.Regular) regularExhausted = true;
        }
    }

    private enum AccessorySlot { Regular, Emphasis, Arms }

    private enum DirectIsolationGap { None, LateralDeltoid, Biceps, Triceps }

    // Internal product conventions (not guideline figures): two direct exposures per week for
    // each of these groups, 3 sets for lateral raises and 2 for arm isolation.
    private const int DirectIsolationWeeklyExposures = 2;
    private const int DirectLateralDeltoidSets = 3;
    private const int DirectArmSets = 2;
    private const int MaximumWeeklyHamstringAccessories = 2;
    private const int ShortSessionMinutes = 45;

    private static DirectIsolationGap GetDirectIsolationGap(TrainingExerciseCandidate candidate)
    {
        if (candidate.MovementPattern != TrainingMovementPattern.Accessory)
            return DirectIsolationGap.None;
        if (candidate.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Deltoids) &&
            (candidate.Emphasis?.Contains(TrainingExerciseEmphasis.LateralDeltoid) ?? false))
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
    // weighted by muscle importance. Glutes count 0.3: every squat, hinge and single-leg lift trains them and the
    // allocator adds those sets after the accessories are chosen. Calves and minor muscles count 0.6.
    private static decimal AccessoryDeficitScore(
        TrainingExerciseCandidate candidate,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> currentEffectiveVolume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        IReadOnlySet<TrainingMuscleGroup>? allowedMuscles,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        var muscles = candidate.StimulatedMuscles()
            .Where(muscle => targets.ContainsKey(muscle) &&
                             (allowedMuscles is null || allowedMuscles.Contains(muscle)))
            .ToArray();
        if (muscles.Length == 0) return 0m;
        return muscles.Average(muscle =>
        {
            var deficit = Math.Max(0m, targets[muscle] - currentEffectiveVolume.GetValueOrDefault(muscle));
            var importance = muscle == TrainingMuscleGroup.Glutes
                ? 0.3m
                : muscle is TrainingMuscleGroup.Chest or TrainingMuscleGroup.Lats or
                    TrainingMuscleGroup.UpperBack or TrainingMuscleGroup.Deltoids or TrainingMuscleGroup.Biceps or
                    TrainingMuscleGroup.Triceps or TrainingMuscleGroup.Hamstrings or TrainingMuscleGroup.Quadriceps
                    ? 1m : 0.6m;
            return deficit * importance;
        });
    }

    /// <summary>
    /// Keep rep ranges and effort targets stable through the mesocycle. Weekly set distribution
    /// is handled by TrainingProgramVolumeAllocator. Deload planning is a separate, explicit
    /// mesocycle decision; this prescription method never imposes a fixed calendar deload.
    /// </summary>
    private static (int Sets, int MinReps, int MaxReps, decimal Rir, int RestSeconds, TrainingProgressionRule Progression)
        PrescriptionFor(TrainingGoal goal, TrainingExperience experience, int week, int daysPerWeek, TrainingMovementPattern pattern,
            bool isPrimaryLift = false, bool isHeavyLift = false, bool isSmallIsolation = false)
    {
        var beginner = experience == TrainingExperience.Beginner;
        // Single-leg work, accessories and core start at two sets: a lone set of lunges or leg extensions is not a
        // training dose. The allocator still adds sets to single-leg lifts as it does to the other compounds.
        if (pattern is TrainingMovementPattern.Accessory or TrainingMovementPattern.Core or TrainingMovementPattern.SingleLeg)
        {
            // Small muscles worked on their own take higher repetitions well, with loads that are easy to handle.
            var high = isSmallIsolation && goal == TrainingGoal.Hypertrophy && !beginner;
            return (2, high ? 12 : goal == TrainingGoal.Strength ? 8 : 10, high ? 20 : 15,
                beginner ? 3m : 2m, pattern == TrainingMovementPattern.Core ? 60 : 90,
                TrainingProgressionRule.DoubleProgression);
        }
        // Internal product heuristic (not an ACSM-prescribed figure): in strength plans the first compound of each
        // session is the session's main lift. It starts at three sets (the allocator may raise it to five) and, on a lift
        // that accepts high loads, uses low repetitions and long rest (beginners stop a little higher, further from
        // failure). Profiles whose equipment has no heavy lift for that slot keep the 5-8 range.
        if (goal == TrainingGoal.Strength && isPrimaryLift)
        {
            if (!isHeavyLift) return (3, 5, 8, beginner ? 3m : 2m, 180, TrainingProgressionRule.DoubleProgression);
            return beginner
                ? (3, 4, 6, 3m, 180, TrainingProgressionRule.DoubleProgression)
                : (3, 3, 6, 2m, 240, TrainingProgressionRule.DoubleProgression);
        }
        // The first heavy lift of an experienced hypertrophy session is done heavier than the rest.
        if (goal == TrainingGoal.Hypertrophy && isPrimaryLift && isHeavyLift && !beginner)
            return (2, 6, 10, 2m, 150, TrainingProgressionRule.DoubleProgression);
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

}

public enum TrainingProgramSplit
{
    Automatic,
    FullBody,
    UpperLower,
    PushPullLegs,
    // Five different sessions: upper and lower, then push, legs and pull (the legs keep push and pull apart). Used by the prebuilt programs.
    Hybrid
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
    bool ContinuesPreviousBlock = false,
    // This block comes after a deload or a preparation: it eases in again, but it is not the first one.
    bool ReturnsFromLighterBlock = false,
    // Seed of the plan, the same for every block. It only orders candidates that are equally good.
    int SelectionSeed = 0,
    // A sequence (asynchronous) plan: the sessions follow one continuous rotation instead of the training days of a week, and
    // TrainingDays is ignored. Null for a calendar plan.
    TrainingSessionSequence? Sequence = null);

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
    bool IsOwnedByCurrentProfessional = false,
    IReadOnlyList<TrainingMuscleGroup>? EffectiveSecondaryMuscleGroups = null,
    IReadOnlyList<TrainingExerciseEmphasis>? Emphasis = null,
    // Body areas for which the exercise is avoided. Read by whoever applies the user's limitations.
    IReadOnlyList<string>? LimitationAreas = null,
    // The techniques the catalog admits on this exercise.
    IReadOnlyList<TrainingTechniqueType>? AllowedTechniques = null);

/// <summary>A part of a muscle group that an exercise trains and that the plan reserves direct work for.</summary>
public enum TrainingExerciseEmphasis
{
    LateralDeltoid
}

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
