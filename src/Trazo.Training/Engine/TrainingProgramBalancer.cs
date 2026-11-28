namespace Trazo.Training;

/// <summary>
/// Keeps two proportions of a unit of sessions: pulling next to pushing, and hamstrings next to quadriceps. The allocator
/// works muscle by muscle and by time, so a plan could end with much more pressing than rowing, or legs almost only for the
/// front of the thigh. Sets are first added to the side that lags, where they fit; where they do not, the leading side gives
/// one back. Every ratio is an internal product heuristic.
/// </summary>
public static class TrainingProgramBalancer
{
    public const decimal MinimumPullPerPush = 0.8m;
    public const decimal MinimumHamstringsPerQuadriceps = 0.5m;
    private const int MinimumSetsLeft = 2;
    private const int MaximumMoves = 60;
    private const decimal AbsoluteWeeklySetCeiling = 20m;

    public static IReadOnlyList<TrainingSessionPrescription> Balance(
        IReadOnlyList<TrainingSessionPrescription> sessions, IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        TrainingGoal goal, int maximumSessionDurationMinutes,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> ceilings, decimal weeksPerGroup = 1m)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);
        var patterns = catalog.GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MovementPattern, StringComparer.OrdinalIgnoreCase);
        var result = new List<TrainingSessionPrescription>();
        foreach (var week in sessions.GroupBy(s => s.WeekNumber))
        {
            var group = week.ToArray();
            // A set added for balance never takes a muscle it trains past its own weekly ceiling, nor any muscle past twenty.
            bool Fits(TrainingExercisePrescription exercise)
            {
                var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(group);
                return exercise.StimulatedMuscles().All(muscle =>
                {
                    var after = (volume.GetValueOrDefault(muscle) + 1m) / weeksPerGroup;
                    return after <= AbsoluteWeeklySetCeiling &&
                           (!exercise.PrimaryMuscleGroups.Contains(muscle) || !ceilings.TryGetValue(muscle, out var ceiling) || after <= ceiling);
                });
            }

            for (var move = 0; move < MaximumMoves; move++)
            {
                if (PullDeficit(group, patterns) > 0m)
                {
                    if (Add(group, e => IsPull(patterns, e) && Fits(e), goal, maximumSessionDurationMinutes)) continue;
                    if (Remove(group, e => IsPush(patterns, e) && !IsMainLift(e, goal))) continue;
                }
                if (HamstringDeficit(group) > 0m)
                {
                    if (Add(group, e => Trains(e, TrainingMuscleGroup.Hamstrings) && !e.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps) && Fits(e),
                            goal, maximumSessionDurationMinutes)) continue;
                    if (Remove(group, e => Trains(e, TrainingMuscleGroup.Quadriceps) && !Trains(e, TrainingMuscleGroup.Hamstrings) && !IsMainLift(e, goal))) continue;
                }
                break;
            }
            result.AddRange(group);
        }
        return result;
    }

    /// <summary>Whether replacing one session leaves one of the two proportions further behind.</summary>
    internal static bool Worsens(
        TrainingSessionPrescription[] week, int index, TrainingSessionPrescription replacement,
        IReadOnlyDictionary<string, TrainingMovementPattern> patterns)
    {
        var after = week.ToArray();
        after[index] = replacement;
        return PullDeficit(after, patterns) > Math.Max(0m, PullDeficit(week, patterns)) ||
               HamstringDeficit(after) > Math.Max(0m, HamstringDeficit(week));
    }

    private static decimal PullDeficit(
        IEnumerable<TrainingSessionPrescription> week, IReadOnlyDictionary<string, TrainingMovementPattern> patterns)
    {
        var exercises = week.SelectMany(s => s.Exercises).ToArray();
        return MinimumPullPerPush * exercises.Where(e => IsPush(patterns, e)).Sum(e => e.Sets)
               - exercises.Where(e => IsPull(patterns, e)).Sum(e => e.Sets);
    }

    private static decimal HamstringDeficit(IEnumerable<TrainingSessionPrescription> week)
    {
        var exercises = week.SelectMany(s => s.Exercises).ToArray();
        return MinimumHamstringsPerQuadriceps * exercises.Where(e => Trains(e, TrainingMuscleGroup.Quadriceps)).Sum(e => e.Sets)
               - exercises.Where(e => Trains(e, TrainingMuscleGroup.Hamstrings)).Sum(e => e.Sets);
    }

    private static bool Add(
        TrainingSessionPrescription[] week, Func<TrainingExercisePrescription, bool> wanted, TrainingGoal goal, int maximumMinutes)
    {
        foreach (var (session, index, exercise) in week
                     .SelectMany((s, i) => s.Exercises.Select(e => (Session: s, Index: i, Exercise: e)))
                     .Where(x => wanted(x.Exercise))
                     .OrderBy(x => x.Exercise.Sets).ThenBy(x => x.Index).ThenBy(x => x.Exercise.Order))
        {
            if (exercise.Sets >= TrainingProgramVolumeAllocator.MaximumSetsFor(exercise, goal, generatedPlan: true) ||
                TrainingProgramVolumeAllocator.ExceedsSessionDose(session, exercise))
                continue;
            var changed = With(session, exercise, exercise.Sets + 1);
            if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(changed) > maximumMinutes * 60m) continue;
            week[index] = changed;
            return true;
        }
        return false;
    }

    private static bool Remove(TrainingSessionPrescription[] week, Func<TrainingExercisePrescription, bool> wanted)
    {
        var pick = week.SelectMany((s, i) => s.Exercises.Select(e => (Session: s, Index: i, Exercise: e)))
            .Where(x => wanted(x.Exercise) && x.Exercise.Sets > MinimumSetsLeft)
            .OrderByDescending(x => x.Exercise.Sets).ThenByDescending(x => x.Exercise.Order).ThenBy(x => x.Index)
            .Select(x => ((TrainingSessionPrescription Session, int Index, TrainingExercisePrescription Exercise)?)x)
            .FirstOrDefault();
        if (pick is not { } found) return false;
        week[found.Index] = With(found.Session, found.Exercise, found.Exercise.Sets - 1);
        return true;
    }

    // The dose of the lift a strength session is built around is decided by the lift, never by these proportions.
    private static bool IsMainLift(TrainingExercisePrescription exercise, TrainingGoal goal) =>
        goal == TrainingGoal.Strength && exercise.Order == 1;

    private static TrainingSessionPrescription With(
        TrainingSessionPrescription session, TrainingExercisePrescription exercise, int sets)
    {
        var changed = session with
        {
            Exercises = session.Exercises.Select(e => e.Order == exercise.Order ? e with { Sets = sets } : e).ToArray()
        };
        return changed with { EstimatedDurationMinutes = (int)Math.Ceiling(TrainingProgramVolumeAllocator.EstimateDurationSeconds(changed) / 60m) };
    }

    private static bool Trains(TrainingExercisePrescription exercise, TrainingMuscleGroup muscle) => exercise.StimulatedMuscles().Contains(muscle);

    private static bool IsPush(IReadOnlyDictionary<string, TrainingMovementPattern> patterns, TrainingExercisePrescription exercise) =>
        patterns.TryGetValue(exercise.ExerciseId, out var p) && p is TrainingMovementPattern.HorizontalPush or TrainingMovementPattern.VerticalPush;

    private static bool IsPull(IReadOnlyDictionary<string, TrainingMovementPattern> patterns, TrainingExercisePrescription exercise) =>
        patterns.TryGetValue(exercise.ExerciseId, out var p) && p is TrainingMovementPattern.HorizontalPull or TrainingMovementPattern.VerticalPull;
}
