using Xunit;
using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>How a simulated user responds to the dose a muscle receives in one week.</summary>
internal enum Response { Good, Neutral, Poor }

/// <summary>What a simulated user does. Every default is a regular, healthy user who answers nothing.</summary>
internal sealed record Behavior
{
    /// <summary>Does the user do the session? (period, index of the session inside the period)</summary>
    public Func<int, int, bool> Attends { get; init; } = (_, _) => true;
    /// <summary>Share of the prescribed sets the user completes in a session (period, index).</summary>
    public Func<int, int, double> Completion { get; init; } = (_, _) => 1.0;
    /// <summary>How a muscle responds to the effective sets it was prescribed that week.</summary>
    public Func<int, TrainingMuscleGroup, decimal, Response> MuscleResponse { get; init; } = (_, _, _) => Response.Good;
    public Func<int, int, bool> Pain { get; init; } = (_, _) => false;
    /// <summary>Answers to the post-exercise questions, or null when the user never answers.</summary>
    public Func<int, TrainingMuscleGroup, (string Stimulus, string Discomfort, string PreFatigue)?> Feedback { get; init; } = (_, _) => null;
    /// <summary>Days after the start of the week on which the sessions of the period are done.</summary>
    public int[] DayOffsets { get; init; } = [0, 2, 4, 5, 6];
}

/// <summary>
/// A plan run in memory: it generates blocks with the real <see cref="MesocycleGenerator"/>, logs workouts the way a user would, and
/// feeds the real observer, estimator and planner (<see cref="LongitudinalEngine"/>) to decide the next block. There is no database,
/// so what a scenario proves is the decision logic that production runs; the SQL side has its own tests.
/// </summary>
internal sealed class PlanSimulator
{
    public static readonly IReadOnlyList<TrainingExerciseCandidate> Catalog = PlanTestCatalog.Candidates();
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    public PlanProfile Profile { get; }
    public int Seed { get; }
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid PlanId { get; } = Guid.NewGuid();
    public List<PlannedSession> Sessions { get; } = [];
    public List<WorkoutLog> Logs { get; } = [];
    public Dictionary<Guid, (int Sequence, bool IsDeload)> Mesocycles { get; } = [];
    public List<MuscleWeekObservation> Observations { get; } = [];
    public List<AppliedTarget> Applied { get; } = [];
    public int NextSequence { get; private set; } = 1;
    public int NextWeek { get; private set; } = 1;
    private int _sequenceIndex;
    private IReadOnlyList<string>? _previousIds;
    private readonly Dictionary<string, decimal> _weights = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, MesocycleGeneration> _generated = [];

    public PlanSimulator(PlanProfile profile, int seed = 7)
    {
        Profile = profile;
        Seed = seed;
    }

    public MesocycleSlot SlotFor(bool deload) => new(NextSequence, 1, deload ? "deload" : "accumulation", NextWeek, deload ? 1 : 4);

    public IReadOnlyList<TrainingMuscleBaseline> Baselines => MesocycleGenerator.Baselines(Profile);

    /// <summary>The block as the engine generates it, with the learned targets if any.</summary>
    public MesocycleGeneration Generate(bool deload, IReadOnlyDictionary<TrainingMuscleGroup, decimal>? learned)
    {
        var generation = MesocycleGenerator.Generate(
            Profile, Seed, SlotFor(deload), Catalog, _previousIds, continuesPreviousBlock: NextSequence > 1, learnedTargets: learned);
        Assert.True(generation.Succeeded, string.Join("; ", generation.Errors));
        return generation;
    }

    /// <summary>What the longitudinal layer decides for the NEXT block from everything logged so far.</summary>
    public LongitudinalResult Decide(DateTimeOffset? now = null)
    {
        RefreshObservations(now ?? Start.AddDays(7 * NextWeek));
        return LongitudinalEngine.Propose(
            PlanId, NextSequence, Baselines, Observations, new Dictionary<Guid, int> { [PlanId] = 0 }, Applied);
    }

    /// <summary>Generates the next block (with the decision's targets), stores the decisions as applied and trains it.</summary>
    public (MesocycleGeneration Block, LongitudinalResult Result) RunBlock(Behavior behavior, bool deload = false, bool learn = true)
    {
        var result = learn && !deload ? Decide() : LongitudinalResult.None;
        var block = Generate(deload, result.LearnedTargets);
        foreach (var d in result.Decisions) Applied.Add(new AppliedTarget(NextSequence, d.Muscle, d.ProposedSets));
        Train(block, behavior, deload);
        return (block, result);
    }

    public void Train(MesocycleGeneration block, Behavior behavior, bool deload = false)
    {
        var mesocycleId = Guid.NewGuid();
        Mesocycles[mesocycleId] = (NextSequence, deload);
        var weeklySets = block.Sessions.GroupBy(s => s.WeekNumber).ToDictionary(
            g => g.Key, g => WeeklyEffective(g.Select(s => PrescriptionJson.Read(s.PrescriptionJson))));
        foreach (var week in block.Sessions.GroupBy(s => s.WeekNumber).OrderBy(g => g.Key))
        {
            var index = 0;
            foreach (var generated in week.OrderBy(s => s.Date).ThenBy(s => s.Title, StringComparer.Ordinal))
            {
                var session = new PlannedSession
                {
                    Id = Guid.NewGuid(), PlanId = PlanId, MesocycleId = mesocycleId, WeekNumber = generated.WeekNumber, Date = generated.Date,
                    SequenceIndex = ++_sequenceIndex, Title = generated.Title, EstimatedDurationMinutes = generated.EstimatedDurationMinutes,
                    PrescriptionJson = generated.PrescriptionJson, Status = PlannedSessionStatus.Planned, UpdatedAt = Start
                };
                Sessions.Add(session);
                if (behavior.Attends(generated.WeekNumber, index)) Log(session, behavior, index, weeklySets[generated.WeekNumber], deload);
                index++;
            }
        }
        _previousIds = block.Sessions.Where(s => s.WeekNumber == block.Sessions.Max(x => x.WeekNumber))
            .SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson).Select(e => e.ExerciseId)).Distinct().ToArray();
        NextSequence++;
        NextWeek += deload ? 1 : 4;
    }

    private void Log(PlannedSession session, Behavior behavior, int index, IReadOnlyDictionary<TrainingMuscleGroup, decimal> weekly, bool deload)
    {
        var period = session.WeekNumber;
        var performedAt = Start.AddDays(7 * (period - 1) + behavior.DayOffsets[Math.Min(index, behavior.DayOffsets.Length - 1)]);
        var prescribed = PrescriptionJson.Read(session.PrescriptionJson);
        var pain = behavior.Pain(period, index);
        var completion = behavior.Completion(period, index);
        var log = new WorkoutLog
        {
            Id = Guid.NewGuid(), UserId = UserId, PlanId = PlanId, PlannedSessionId = session.Id, LoggedAt = performedAt.AddHours(1),
            PerformedAt = performedAt, PerformedOn = DateOnly.FromDateTime(performedAt.UtcDateTime), TimeZoneId = "UTC",
            Outcome = completion >= 0.999 ? WorkoutOutcome.Completed : WorkoutOutcome.Partial,
            Recovery = "Good", PainReported = pain
        };
        var fatigued = false;
        var answers = new List<object>();
        foreach (var exercise in prescribed)
        {
            var muscle = exercise.PrimaryMuscleGroups.First();
            var response = deload ? Response.Neutral : behavior.MuscleResponse(period, muscle, weekly.GetValueOrDefault(muscle));
            var gain = response switch { Response.Good => 0.03m, Response.Poor => -0.04m, _ => 0m };
            if (response == Response.Poor) fatigued = true;
            _weights.TryAdd(exercise.ExerciseId, 20m + Math.Abs(exercise.ExerciseId.GetHashCode() % 40));
            var done = (int)Math.Round(exercise.Sets * completion);
            if (done > 0) _weights[exercise.ExerciseId] = Math.Round(_weights[exercise.ExerciseId] * (1m + gain), 2);
            for (var set = 1; set <= exercise.Sets; set++)
                log.Sets.Add(new WorkoutSetLog
                {
                    Id = Guid.NewGuid(), WorkoutLogId = log.Id, ExerciseId = exercise.ExerciseId, SetNumber = set,
                    WeightKg = _weights[exercise.ExerciseId], Repetitions = (exercise.MinRepetitions + exercise.MaxRepetitions) / 2,
                    Rir = 2m, Completed = set <= done, SkippedReason = set <= done ? null : "time", PainReported = pain && set == 1
                });
            if (behavior.Feedback(period, muscle) is { } f)
                answers.Add(new { exerciseId = exercise.ExerciseId, stimulus = f.Stimulus, discomfort = f.Discomfort, preFatigue = f.PreFatigue });
        }
        if (fatigued) { log.Recovery = "Insufficient"; log.SessionRpe = 9.5m; }
        if (answers.Count > 0) log.ExerciseFeedbackJson = System.Text.Json.JsonSerializer.Serialize(answers, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Logs.Add(log);
        session.Status = log.Outcome == WorkoutOutcome.Completed ? PlannedSessionStatus.Completed : PlannedSessionStatus.Partial;
    }

    /// <summary>Rebuilds the observations of every period that has sessions, like the service does for the stale ones.</summary>
    public void RefreshObservations(DateTimeOffset now)
    {
        var periods = Sessions.Select(s => s.WeekNumber).Distinct().ToList();
        var built = MuscleWeekObserver.Build(new ObserverInput(UserId, PlanId, Sessions, Logs, Mesocycles, periods, now));
        foreach (var row in built)
        {
            var existing = Observations.FirstOrDefault(o => o.Period == row.Period && o.Muscle == row.Muscle);
            if (existing is null) Observations.Add(row);
            else existing.CopyFrom(row);
        }
    }

    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> WeeklyEffective(IEnumerable<IReadOnlyList<TrainingExercisePrescription>> sessions)
    {
        var total = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(s => s))
            foreach (var muscle in TrainingExerciseStimulus.GetEffectiveMuscles(exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups))
                total[muscle] = total.GetValueOrDefault(muscle) + exercise.Sets;
        return total;
    }

    /// <summary>Weekly effective sets of every muscle in one week of a generated block.</summary>
    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> VolumeOfWeek(MesocycleGeneration block, int weekIndexInBlock = 2) =>
        block.Quality!.WeeklyMuscleVolumes.First(w => w.WeekNumber == weekIndexInBlock).Muscles
            .ToDictionary(m => m.MuscleGroup, m => m.EstimatedEffectiveSets);
}
