using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>A clock the simulation moves forward.</summary>
public sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Whole plans lived through the real API: a simulated person trains every session with their own strength, which the engine
/// never sees, and the engine answers with loads, sets and adjustments. What is asserted is that those answers stay sensible for
/// every kind of person, block after block. It proves how the engine behaves, not that anybody grows.
/// <para>The suite runs a few of those lives; set <c>TRAZO_SIM_FULL=1</c> for all of them (about four minutes) and
/// <c>TRAZO_SIM_REPORT</c> to a file path to read the table of results.</para>
/// </summary>
public sealed class EngineSimulationTests : PlansApiBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly AdjustableClock Clock = new(Start);
    private readonly ITestOutputHelper _output;

    public EngineSimulationTests(ITestOutputHelper output) : base(enforced: false, clock: Clock) => _output = output;

    /// <summary>How a simulated person is and behaves.</summary>
    public sealed record Athlete(
        string Name,
        double WeeklyGain = 0.006,
        double Noise = 0,
        int RirBias = 0,
        bool WritesRir = true,
        double SkipsLastSet = 0,
        double SetFatigue = 0,
        double StartBias = 1.0,
        int PainEvery = 0,
        double BadDay = 0,
        bool Answers = false,
        bool FollowsTheLoad = true);

    private static readonly Athlete[] Athletes =
    [
        new("constant"),
        new("novice", WeeklyGain: 0.02),
        new("stalled", WeeklyGain: 0),
        new("noisy", Noise: 0.04),
        new("optimist", RirBias: 2),
        new("pessimist", RirBias: -1),
        new("no-rir", WritesRir: false),
        new("busy", SkipsLastSet: 0.4),
        new("tires", SetFatigue: 0.7),
        new("too-heavy", StartBias: 1.3),
        new("too-light", StartBias: 0.6),
        new("bad-weeks", BadDay: 0.15),
        new("answers", Answers: true),
        new("sore", PainEvery: 9),
        new("stubborn", FollowsTheLoad: false),
    ];

    private sealed class Exercise
    {
        public double OneRepMax;
        public double? Load;
        public int Asked;
        public int Exposures;
        public double? LastTarget;
        public int Min, Max;
        public double Rir;
        public int Increases, Reductions;
    }

    private sealed record Finding(string Athlete, string Plan, string Rule, string Detail);

    private sealed class Tally
    {
        public int Sessions, Increases, Reductions, Exercises, Underloaded, Overloaded;
        public readonly Dictionary<string, int> Applied = new(StringComparer.Ordinal);
        public double Gap;
    }

    private static int StableHash(string text) => text.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff;

    private static double Increment(double load) => load < 20 ? 1 : 2.5;

    private static double RoundTo(double load) => Math.Max(Increment(load), Math.Round(load / Increment(load)) * Increment(load));

    private async Task<Tally> LiveAsync(Athlete athlete, string goal, string experience, int sessionsPerPeriod, int seed, List<Finding> findings)
    {
        Clock.Now = Start;
        var random = new Random(seed);
        var plan = $"{goal}/{experience}/{sessionsPerPeriod}d/s{seed}";
        void Report(string rule, string detail) => findings.Add(new Finding(athlete.Name, plan, rule, detail));
        var client = ClientFor(await AddUserAsync($"{athlete.Name}-{goal}-{experience}-{sessionsPerPeriod}-{seed}@sim.test"));
        var created = await client.PostAsJsonAsync("/api/v1/plans", new
        {
            goal, experience, totalWeeks = 12, schedulingMode = "Sequence", sessionsPerPeriod, maxSessionMinutes = 75,
            availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
            excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>()
        });
        if (created.StatusCode != HttpStatusCode.Created) throw new InvalidOperationException($"{plan} {athlete.Name}: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");

        var tally = new Tally();
        var exercises = new Dictionary<string, Exercise>(StringComparer.Ordinal);
        var weeklyGapDays = 7.0 / sessionsPerPeriod;
        for (var n = 0; n < 12 * sessionsPerPeriod + 2; n++)
        {
            var answer = await client.GetAsync("/api/v1/plans/current/next");
            if (answer.StatusCode == HttpStatusCode.NotFound) break;
            if (answer.StatusCode != HttpStatusCode.OK) { Report("http", $"next: {(int)answer.StatusCode} {await answer.Content.ReadAsStringAsync()}"); break; }
            var next = await JsonAsync(answer);
            if (!next.TryGetProperty("session", out var session) || session.ValueKind != JsonValueKind.Object) break;
            var weeks = (Clock.Now - Start).TotalDays / 7;
            var sets = new List<object>();
            var feedback = new Dictionary<string, object>();
            var hurts = athlete.PainEvery > 0 && n > 0 && n % athlete.PainEvery == 0;
            var badDay = random.NextDouble() < athlete.BadDay;
            var skipped = false;
            var first = true;
            foreach (var prescribed in session.GetProperty("exercises").EnumerateArray())
            {
                var id = prescribed.GetProperty("exerciseId").GetString()!;
                var count = prescribed.GetProperty("sets").GetInt32();
                var min = prescribed.GetProperty("minRepetitions").GetInt32();
                var max = prescribed.GetProperty("maxRepetitions").GetInt32();
                var rir = prescribed.TryGetProperty("targetRir", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : 2;
                var target = prescribed.TryGetProperty("targetLoadKg", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : (double?)null;
                if (count is < 1 or > 8) Report("sets", $"{id} prescribes {count} sets");
                if (!exercises.TryGetValue(id, out var state))
                    exercises[id] = state = new Exercise { OneRepMax = 14 + StableHash(id) % 130 };
                var today = state.OneRepMax * Math.Pow(1 + athlete.WeeklyGain, weeks)
                    * (1 + athlete.Noise * (random.NextDouble() * 2 - 1)) * (badDay ? 0.9 : 1);
                int Capacity(double load) => (int)Math.Floor(30 * (today / load - 1));

                // A target the person could not even lift to the bottom of the range, after the engine has seen them a few times.
                if (target is { } wanted && state.Exposures >= 3 && athlete is { RirBias: 0, WritesRir: true, FollowsTheLoad: true, BadDay: 0 }
                    && athlete.PainEvery == 0 && Capacity(wanted) < min - 2)
                    Report("overload", $"{id} session {n}: target {wanted} kg allows {Capacity(wanted)} reps, range {min}-{max}");
                if (target is { } now && state.LastTarget is { } before && now - before > Math.Max(Increment(before), before * 0.101) + 0.01)
                {
                    // A new block may prescribe another range: then the load is estimated afresh.
                    if (state.Min == min && state.Max == max) Report("jump", $"{id} session {n}: {before} -> {now} kg at {min}-{max}");
                }
                if (target is { } t2 && state.LastTarget is { } b2 && state.Min == min && state.Max == max)
                {
                    if (t2 > b2) { state.Increases++; tally.Increases++; }
                    if (t2 < b2) { state.Reductions++; tally.Reductions++; }
                }

                var load = athlete.FollowsTheLoad && target is { } follow ? follow
                    : state.Load ?? RoundTo(today / (1 + ((min + max) / 2.0 + rir) / 30) * athlete.StartBias);
                // A person lowers a weight they cannot move into the range.
                for (var tries = 0; tries < 8 && Capacity(load) < min - 1 && load > Increment(load); tries++) load = RoundTo(load * 0.93);

                var asked = state.Min == min && state.Max == max ? state.Asked : 0;
                for (var set = 1; set <= count; set++)
                {
                    if (set == count && count > 1 && random.NextDouble() < athlete.SkipsLastSet)
                    {
                        sets.Add(new { exerciseId = id, setNumber = set, weightKg = (double?)load, repetitions = (int?)null, rir = (int?)null, completed = false, skippedReason = (string?)"time", painReported = false });
                        skipped = true;
                        continue;
                    }
                    var capacity = Capacity(load) - (int)Math.Floor(athlete.SetFatigue * (set - 1));
                    var reps = Math.Clamp((int)Math.Round(capacity - rir), 0, Math.Max(max, asked));
                    if (reps < 1) reps = Math.Clamp(capacity, 0, max);
                    if (reps < 1)
                    {
                        sets.Add(new { exerciseId = id, setNumber = set, weightKg = (double?)load, repetitions = (int?)null, rir = (int?)null, completed = false, skippedReason = (string?)"load_too_heavy", painReported = false });
                        skipped = true;
                        continue;
                    }
                    int? written = athlete.WritesRir ? Math.Clamp(capacity - reps + athlete.RirBias, 0, 6) : null;
                    sets.Add(new { exerciseId = id, setNumber = set, weightKg = (double?)load, repetitions = (int?)reps, rir = written, completed = true, skippedReason = (string?)null, painReported = hurts && first && set == 1 });
                }
                if (athlete.Answers) feedback[id] = new { stimulus = random.NextDouble() < 0.4 ? "low" : "adequate", discomfort = "none", preFatigue = random.NextDouble() < 0.2 ? "loaded" : "recovered" };
                state.Load = load;
                state.LastTarget = target ?? load;
                state.Min = min;
                state.Max = max;
                state.Rir = rir;
                state.Exposures++;
                first = false;
            }

            var sessionId = session.GetProperty("id").GetString();
            var logged = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log", new
            {
                outcome = skipped ? "Partial" : "Completed", nonCompletionReason = skipped ? "Time" : "None",
                recovery = badDay ? "Insufficient" : "Good", sessionRpe = badDay ? 9.5 : 7.5, painReported = hurts, sets,
                exerciseFeedback = feedback.Count > 0 ? feedback : null
            });
            if (logged.StatusCode != HttpStatusCode.OK) { Report("http", $"log {n}: {(int)logged.StatusCode} {await logged.Content.ReadAsStringAsync()}"); break; }
            tally.Sessions++;
            var adaptation = (await JsonAsync(logged)).GetProperty("adaptation");
            foreach (var advice in adaptation.GetProperty("exercises").EnumerateArray())
            {
                var id = advice.GetProperty("exerciseId").GetString()!;
                if (!exercises.TryGetValue(id, out var state)) continue;
                state.Asked = advice.TryGetProperty("suggestedRepetitions", out var reps) && reps.ValueKind == JsonValueKind.Array && reps.GetArrayLength() > 0
                    ? reps.EnumerateArray().Max(x => x.GetInt32()) : 0;
                var action = advice.GetProperty("action").GetString()!;
                var nextLoad = advice.TryGetProperty("nextLoadKg", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetDouble() : (double?)null;
                if (hurts && action == "IncreaseLoad") Report("pain", $"{id} session {n}: load raised after a painful session ({nextLoad})");
                if (nextLoad is <= 0) Report("load", $"{id} session {n}: next load {nextLoad}");
                if (action == "InvalidData" || action == "InvalidPrescription") Report("invalid", $"{id} session {n}: {action} {advice.GetProperty("explanation").GetString()}");
            }
            foreach (var applied in adaptation.GetProperty("applied").EnumerateArray())
            {
                var kind = applied.GetProperty("kind").GetString()!;
                tally.Applied[kind] = tally.Applied.GetValueOrDefault(kind) + 1;
            }
            Clock.Now = Clock.Now.AddDays(weeklyGapDays);
        }

        if (tally.Sessions < 10 * sessionsPerPeriod) Report("unfinished", $"only {tally.Sessions} sessions of about {12 * sessionsPerPeriod}");
        var weeksLived = (Clock.Now - Start).TotalDays / 7;
        foreach (var (id, state) in exercises.Where(x => x.Value.Exposures >= 8 && x.Value.Load is not null))
        {
            // The load at which this person, today, reaches the middle of the range with the reserve asked for.
            var ideal = state.OneRepMax * Math.Pow(1 + athlete.WeeklyGain, weeksLived) / (1 + ((state.Min + state.Max) / 2.0 + state.Rir) / 30);
            var ratio = state.Load!.Value / ideal;
            tally.Exercises++;
            tally.Gap += Math.Abs(1 - ratio);
            if (ratio < 0.8 && state.Load.Value + 2 * Increment(state.Load.Value) < ideal) tally.Underloaded++;
            if (ratio > 1.2) tally.Overloaded++;
        }
        return tally;
    }

    [Fact]
    public async Task PlansLivedThroughTheApiStaySensibleForEveryKindOfPerson()
    {
        var full = Environment.GetEnvironmentVariable("TRAZO_SIM_FULL") == "1";
        var athletes = full ? Athletes : Athletes.Where(a => a.Name is "constant" or "noisy" or "too-heavy" or "sore").ToArray();
        (string, string, int, int)[] plans = [("Hypertrophy", "Beginner", 3, 1), ("Hypertrophy", "Intermediate", 4, 2), ("Strength", "Intermediate", 3, 3), ("Hypertrophy", "Advanced", 4, 4)];
        if (!full) plans = [plans[2]];
        var findings = new List<Finding>();
        var table = new StringBuilder("athlete | plan | sessions | up | down | exercises | under | over | gap | adjustments\n");
        foreach (var athlete in athletes)
        foreach (var (goal, experience, days, seed) in plans)
        {
            var t = await LiveAsync(athlete, goal, experience, days, seed, findings);
            // A regular person who writes what they do ends the plan lifting about what they can.
            if (athlete.Name is "constant" or "stalled" or "noisy" && (t.Underloaded > 0 || t.Overloaded > 0 || t.Gap / Math.Max(1, t.Exercises) > 0.10))
                findings.Add(new Finding(athlete.Name, $"{goal}/{experience}", "far-from-capacity", $"under {t.Underloaded}, over {t.Overloaded}, gap {t.Gap / Math.Max(1, t.Exercises):P0}"));
            if (athlete.Name is "constant" or "novice" && t.Increases == 0)
                findings.Add(new Finding(athlete.Name, $"{goal}/{experience}", "never-progresses", "no load increase in the whole plan"));
            table.AppendLine($"{athlete.Name} | {goal[..3]}-{experience[..3]}-{days} | {t.Sessions} | {t.Increases} | {t.Reductions} | {t.Exercises} | {t.Underloaded} | {t.Overloaded} | {(t.Exercises == 0 ? 0 : t.Gap / t.Exercises):P0} | {string.Join(" ", t.Applied.OrderBy(a => a.Key).Select(a => $"{a.Key}={a.Value}"))}");
        }
        var report = table + "\n" + string.Join("\n", findings.GroupBy(f => f.Rule).Select(g => $"## {g.Key}: {g.Count()}\n" + string.Join("\n", g.Take(25).Select(f => $"- {f.Athlete} {f.Plan}: {f.Detail}"))));
        _output.WriteLine(report);
        if (Environment.GetEnvironmentVariable("TRAZO_SIM_REPORT") is { Length: > 0 } path) File.WriteAllText(path, report);

        Assert.True(findings.Count == 0, $"{findings.Count} findings:\n" + string.Join("\n", findings.Take(40)));
    }
}
