using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// Asking the generator for techniques: only done when the app making the request can show them. It carries the exercises with
/// recent pain, which never receive one.
/// </summary>
public sealed record TechniqueAllowance(IReadOnlyCollection<string> PainfulExercises)
{
    public static TechniqueAllowance NoPain { get; } = new(Array.Empty<string>());
}

/// <summary>Why a generated technique or superset is there; the app shows it in the user's language.</summary>
public static class TechniqueReasons
{
    public const string PriorityMuscle = "PriorityMuscle";
    public const string ShortOnTime = "ShortOnTime";
}

/// <summary>One generated session before it is stored: its exercises and how the supersets among them are alternated.</summary>
public sealed record TechniqueSession(
    int WeekNumber, IReadOnlyList<TrainingExercisePrescription> Exercises, int EstimatedDurationMinutes,
    IReadOnlyDictionary<string, string> SupersetGroups, IReadOnlyList<TrainingSupersetPrescription> Supersets);

/// <summary>
/// Adds techniques to a generated week, and only for two reasons (docs/prescription-techniques.md, matrix R4): the user asked to
/// emphasise a muscle, or the session has no time left for the sets a muscle is missing. The justification is time, never a claim
/// that a technique grows more muscle than straight sets. Hypertrophy plans of intermediate and advanced users only, on
/// isolation exercises the catalog admits; never on compound lifts, with pain, or on a muscle whose dose was lowered by the
/// user's history. Every figure is a product decision.
/// </summary>
public static class TechniquePlanner
{
    public const int MaximumTechniquesPerSession = 2;
    private const int MinimumSetsForTechnique = 2;
    private const int MaximumSupersetRounds = 5;
    private const decimal AbsoluteWeeklySets = 20m;

    /// <summary>Drop sets first (the best supported); the others only for advanced users, when a drop set is not admitted.</summary>
    private static readonly TrainingTechniqueType[] AdvancedPreference =
        [TrainingTechniqueType.DropSet, TrainingTechniqueType.MyoReps, TrainingTechniqueType.RestPause];

    private static readonly TrainingTechniqueType[] IntermediatePreference = [TrainingTechniqueType.DropSet];

    /// <summary>Whether this profile can receive techniques at all.</summary>
    public static bool Applies(PlanProfile profile) =>
        profile.Goal == TrainingGoal.Hypertrophy && profile.Experience != TrainingExperience.Beginner;

    /// <param name="week">The sessions of ONE week (or period) of a training block, in program order.</param>
    /// <param name="loweredMuscles">Muscles whose weekly target was lowered by the user's history: they get nothing extra.</param>
    public static IReadOnlyList<TechniqueSession> ApplyToWeek(
        PlanProfile profile,
        IReadOnlyList<TechniqueSession> week,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        TechniqueAllowance allowance,
        IReadOnlyCollection<TrainingMuscleGroup>? loweredMuscles = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(allowance);
        if (!Applies(profile) || week.Count == 0) return week;

        var context = new Context(profile, catalog, allowance, loweredMuscles ?? []);
        var sessions = week.ToArray();
        for (var index = 0; index < sessions.Length; index++)
            sessions[index] = context.Superset(sessions, index);
        var used = new HashSet<TrainingMuscleGroup>();
        // Emphasised muscles choose first, across the whole week; time-saving takes what is left.
        foreach (var reason in new[] { TechniqueReasons.PriorityMuscle, TechniqueReasons.ShortOnTime })
            for (var index = 0; index < sessions.Length; index++)
                sessions[index] = context.Techniques(sessions, index, reason, used);
        return sessions;
    }

    private sealed class Context
    {
        private readonly PlanProfile profile;
        private readonly Dictionary<string, TrainingExerciseCandidate> candidates;
        private readonly HashSet<string> painful;
        private readonly HashSet<TrainingMuscleGroup> lowered;
        private readonly Dictionary<TrainingMuscleGroup, decimal> targets;
        private readonly HashSet<TrainingMuscleGroup> emphasised;
        private readonly decimal tolerance;

        public Context(
            PlanProfile profile, IReadOnlyCollection<TrainingExerciseCandidate> catalog, TechniqueAllowance allowance,
            IReadOnlyCollection<TrainingMuscleGroup> loweredMuscles)
        {
            this.profile = profile;
            candidates = catalog.GroupBy(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            painful = allowance.PainfulExercises.ToHashSet(StringComparer.OrdinalIgnoreCase);
            lowered = loweredMuscles.ToHashSet();
            targets = MesocycleGenerator.Baselines(profile).ToDictionary(b => b.Muscle, b => b.Target);
            emphasised = (profile.MusclePriorities ?? new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>())
                .Where(p => p.Value == TrainingMusclePriority.Emphasize).Select(p => p.Key).ToHashSet();
            // Half-width of the planning range of each level, the same the allocator accepts.
            tolerance = profile.Experience == TrainingExperience.Intermediate ? 3m : 4m;
        }

        private decimal MaximumSeconds => profile.MaxSessionMinutes * 60m;

        private static decimal Seconds(TechniqueSession session)
        {
            var seconds = TrainingProgramVolumeAllocator.EstimateDurationSeconds(
                new TrainingSessionPrescription(1, DayOfWeek.Monday, "-", 1, session.Exercises));
            foreach (var exercise in session.Exercises)
                seconds += TechniqueRules.ExtraSeconds(exercise.Technique, exercise.Sets);
            // In a superset the rests are those of the round, not those between the sets of each exercise.
            foreach (var superset in session.Supersets)
            {
                var members = session.Exercises
                    .Where(e => session.SupersetGroups.TryGetValue(e.ExerciseId, out var g) && g == superset.Group).ToList();
                if (members.Count == 0) continue;
                seconds -= members.Sum(e => Math.Max(0, e.Sets - 1) * e.RestSeconds);
                var rounds = Math.Min(superset.Rounds, members.Max(m => m.Sets));
                seconds += rounds * ((members.Count - 1) * superset.RestBetweenSeconds + superset.RestAfterRoundSeconds);
            }
            return seconds;
        }

        private static TechniqueSession WithDuration(TechniqueSession session) =>
            session with { EstimatedDurationMinutes = (int)Math.Ceiling(Seconds(session) / 60m) };

        private static IReadOnlyDictionary<TrainingMuscleGroup, decimal> Volume(IEnumerable<TechniqueSession> week) =>
            TrainingProgramVolumeAllocator.CalculateEffectiveVolume(
                week.Select(s => new TrainingSessionPrescription(1, DayOfWeek.Monday, "-", 1, s.Exercises)));

        /// <summary>A muscle that is under its range this week.</summary>
        private bool IsShort(TrainingMuscleGroup muscle, IReadOnlyDictionary<TrainingMuscleGroup, decimal> volume) =>
            targets.TryGetValue(muscle, out var target) && !lowered.Contains(muscle) &&
            volume.GetValueOrDefault(muscle) < target - tolerance;

        /// <summary>No time left in the session for one more straight set of the exercise.</summary>
        private bool NoRoomForAnotherSet(TechniqueSession session, TrainingExercisePrescription exercise) =>
            Seconds(WithSets(session, exercise.ExerciseId, exercise.Sets + 1)) > MaximumSeconds;

        private static TechniqueSession WithSets(TechniqueSession session, string exerciseId, int sets) => session with
        {
            Exercises = session.Exercises.Select(e => e.ExerciseId == exerciseId ? e with { Sets = sets } : e).ToArray()
        };

        private bool IsIsolation(TrainingExercisePrescription exercise) =>
            candidates.TryGetValue(exercise.ExerciseId, out var candidate) &&
            candidate.MovementPattern == TrainingMovementPattern.Accessory && !painful.Contains(exercise.ExerciseId);

        /// <summary>
        /// Joins two isolation exercises that share no muscle, only when the time it frees buys one more set for a muscle that is
        /// short and had no room for it. At most one superset per session.
        /// </summary>
        public TechniqueSession Superset(IReadOnlyList<TechniqueSession> week, int index)
        {
            var session = week[index];
            if (session.Supersets.Count > 0) return session;
            var volume = Volume(week);
            var maximumSets = TrainingProgramVolumeAllocator.GetMaximumSetsPerExercise(profile.Goal);
            var needing = session.Exercises.OrderBy(e => e.Order)
                .Where(e => e.Sets < maximumSets && e.PrimaryMuscleGroups.Any(m => IsShort(m, volume)) &&
                            !painful.Contains(e.ExerciseId) && NoRoomForAnotherSet(session, e) &&
                            e.StimulatedMuscles().All(m => !targets.TryGetValue(m, out var target) ||
                                !lowered.Contains(m) && volume.GetValueOrDefault(m) + 1m <= Math.Min(AbsoluteWeeklySets, target + tolerance)))
                .ToArray();
            if (needing.Length == 0) return session;

            var isolation = session.Exercises.OrderBy(e => e.Order)
                .Where(e => IsIsolation(e) && e.Technique is null && e.Sets <= MaximumSupersetRounds).ToArray();
            var pairs = (from i in Enumerable.Range(0, isolation.Length)
                         from j in Enumerable.Range(i + 1, isolation.Length - i - 1)
                         let first = isolation[i]
                         let second = isolation[j]
                         where !first.StimulatedMuscles().Overlaps(second.StimulatedMuscles())
                         let joined = Join(session, first, second)
                         orderby Seconds(joined), first.Order, second.Order
                         select (first, second)).ToArray();
            foreach (var (first, second) in pairs)
            {
                foreach (var target in needing)
                {
                    if (target.Sets + 1 > MaximumSupersetRounds) continue;
                    var raised = WithSets(session, target.ExerciseId, target.Sets + 1);
                    var joined = Join(raised, raised.Exercises.First(e => e.ExerciseId == first.ExerciseId),
                        raised.Exercises.First(e => e.ExerciseId == second.ExerciseId));
                    if (Seconds(joined) <= MaximumSeconds) return WithDuration(joined);
                }
            }
            return session;
        }

        /// <summary>The two exercises next to each other, alternated by rounds.</summary>
        private static TechniqueSession Join(
            TechniqueSession session, TrainingExercisePrescription first, TrainingExercisePrescription second)
        {
            var ordered = session.Exercises.OrderBy(e => e.Order).Where(e => e.ExerciseId != second.ExerciseId).ToList();
            ordered.Insert(ordered.FindIndex(e => e.ExerciseId == first.ExerciseId) + 1, second);
            var superset = new TrainingSupersetPrescription(
                "A", Math.Max(first.Sets, second.Sets), TechniqueRules.DefaultRestBetweenSeconds,
                Math.Max(TechniqueRules.DefaultRestAfterRoundSeconds, Math.Max(first.RestSeconds, second.RestSeconds)),
                TechniqueReasons.ShortOnTime);
            return session with
            {
                Exercises = ordered.Select((e, i) => e with { Order = i + 1 }).ToArray(),
                SupersetGroups = new Dictionary<string, string> { [first.ExerciseId] = "A", [second.ExerciseId] = "A" },
                Supersets = [superset]
            };
        }

        /// <summary>Gives the exercises that qualify for <paramref name="reason"/> a technique, within the caps.</summary>
        public TechniqueSession Techniques(
            IReadOnlyList<TechniqueSession> week, int index, string reason, HashSet<TrainingMuscleGroup> used)
        {
            var session = week[index];
            var volume = Volume(week);
            var preference = profile.Experience == TrainingExperience.Advanced ? AdvancedPreference : IntermediatePreference;
            foreach (var id in session.Exercises.OrderBy(e => e.Order).Select(e => e.ExerciseId).ToArray())
            {
                if (session.Exercises.Count(e => e.Technique is not null) >= MaximumTechniquesPerSession) break;
                var exercise = session.Exercises.First(e => e.ExerciseId == id);
                if (exercise.Technique is not null || exercise.Sets < MinimumSetsForTechnique ||
                    session.SupersetGroups.ContainsKey(id) || !IsIsolation(exercise)) continue;
                var muscles = exercise.PrimaryMuscleGroups;
                // One technique per muscle and week, and nothing extra for a muscle the history says to train less.
                if (muscles.Any(used.Contains) || exercise.StimulatedMuscles().Any(lowered.Contains)) continue;
                var qualifies = reason == TechniqueReasons.PriorityMuscle
                    ? muscles.Any(emphasised.Contains)
                    : muscles.Any(m => IsShort(m, volume)) && NoRoomForAnotherSet(session, exercise);
                if (!qualifies) continue;

                var allowed = candidates[id].AllowedTechniques ?? [];
                foreach (var type in preference.Where(allowed.Contains))
                {
                    var technique = TechniqueRules.Default(type) with { Reason = reason };
                    var changed = session with
                    {
                        Exercises = session.Exercises.Select(e => e.ExerciseId == id ? e with { Technique = technique } : e).ToArray()
                    };
                    if (Seconds(changed) > MaximumSeconds) continue;
                    session = WithDuration(changed);
                    foreach (var muscle in muscles) used.Add(muscle);
                    break;
                }
            }
            return session;
        }
    }
}
