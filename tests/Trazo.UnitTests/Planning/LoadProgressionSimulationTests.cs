using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Load progression end to end on blocks of the real generator: a simulated lifter does every set at the top of the range with the
/// prescribed reserve (more repetitions when the engine asks for them), uses the load the engine proposes, and the engine reads each workout as the service does. Someone who always
/// succeeds must be offered more load, whatever the weight and however the block changes sets and reserve from week to week.
/// </summary>
public sealed class LoadProgressionSimulationTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private sealed record Track(decimal First, decimal Last, int Exposures);

    private static Dictionary<string, Track> Train(PlanProfile profile, decimal startKg, int blocks)
    {
        var history = new List<ExerciseExposureRecord>();
        var sessionRecords = new List<SessionRecord>();
        var loads = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var asked = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<string>? previous = null;
        var day = 0;
        for (var block = 0; block < blocks; block++)
        {
            var generation = MesocycleGenerator.Generate(
                profile, 7, new MesocycleSlot(block + 1, 1, "accumulation", 1 + block * 4, 4), PlanTestCatalog.Candidates(), previous,
                continuesPreviousBlock: block > 0);
            Assert.True(generation.Succeeded, string.Join("; ", generation.Errors));
            foreach (var session in generation.Sessions.OrderBy(s => s.WeekNumber).ThenBy(s => s.Date))
            {
                var at = Start.AddDays(day += 2);
                var exercises = PrescriptionJson.Read(session.PrescriptionJson);
                foreach (var e in exercises)
                {
                    loads.TryAdd(e.ExerciseId, startKg);
                    counts[e.ExerciseId] = counts.GetValueOrDefault(e.ExerciseId) + 1;
                    var sets = Enumerable.Range(0, e.Sets).Select(_ => new SetRecord(
                        loads[e.ExerciseId], Math.Max(e.MaxRepetitions, asked.GetValueOrDefault(e.ExerciseId)), e.TargetRepetitionsInReserve ?? 2m, true, null, false)).ToArray();
                    history.Add(new ExerciseExposureRecord(at, e.ExerciseId, e.Sets, e.MinRepetitions, e.MaxRepetitions,
                        e.TargetRepetitionsInReserve, e.ProgressionRule, true, sets, WorkoutOutcome.Completed, "Good"));
                }
                sessionRecords.Add(new SessionRecord(at, WorkoutOutcome.Completed, "None", "Good", false, 7, 1m));
                var outcome = AdaptationPlanner.Evaluate(new AdaptationInput(
                    history, sessionRecords, exercises.Select(e => e.ExerciseId).ToArray(), [], at));
                foreach (var advice in outcome.Exercises)
                {
                    asked[advice.ExerciseId] = advice.SuggestedRepetitions is { Count: > 0 } reps ? reps.Max() : 0;
                    if (advice.NextLoadKg is not { } next) continue;
                    var step = TrainingLoadEstimator.IncrementFor(loads[advice.ExerciseId]);
                    Assert.True(next - loads[advice.ExerciseId] <= step, $"{advice.ExerciseId} jumped from {loads[advice.ExerciseId]} to {next}");
                    loads[advice.ExerciseId] = next;
                }
            }
            previous = generation.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson).Select(e => e.ExerciseId)).Distinct().ToArray();
        }
        return loads.ToDictionary(l => l.Key, l => new Track(startKg, l.Value, counts[l.Key]), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void ALifterWhoAlwaysSucceedsIsOfferedMoreLoad(int startKg)
    {
        var result = Train(PlanTestCatalog.Profile(), startKg, blocks: 3);

        foreach (var (id, t) in result.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.WriteLine($"{id}: {t.First} -> {t.Last} kg in {t.Exposures} sessions");
        // Eight sessions of an exercise leave room for at least one increase even with the two-session confirmation.
        var trained = result.Where(x => x.Value.Exposures >= 8).ToList();
        Assert.NotEmpty(trained);
        var stuck = trained.Where(x => x.Value.Last <= x.Value.First).Select(x => $"{x.Key} ({x.Value.Exposures} sessions)").ToList();
        Assert.True(stuck.Count == 0, $"from {startKg} kg no increase for: {string.Join(", ", stuck)}");
    }
}
