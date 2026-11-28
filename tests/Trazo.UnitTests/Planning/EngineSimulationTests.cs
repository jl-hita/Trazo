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
        bool FollowsTheLoad = true,
        /// <summary>Answers about each muscle: "fresh" (recovered, little work), "tired" (not recovered, too much) or
        /// "honest" (not recovered on a bad day, fine otherwise). Null answers nothing.</summary>
        string? Muscles = null,
        /// <summary>False for the app that no longer asks about recovery in the finish dialog.</summary>
        bool SendsRecovery = true);

    private static readonly Athlete[] Athletes =
    [
        new("constant"),
        new("novice", WeeklyGain: 0.02),
        new("stalled", WeeklyGain: 0),
        new("noisy", Noise: 0.04),
        new("optimist", RirBias: 2),
        new("pessimist", RirBias: -1),
        // Really losing performance, and feeling it: the case where taking a set away is right.
        new("wearing-down", WeeklyGain: -0.02, RirBias: -1),
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
        public int Sessions, Increases, Reductions, Exercises, Underloaded, Overloaded, CompletedSets, ShortSets;
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
            var muscles = new Dictionary<string, object>();
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
                    tally.CompletedSets++;
                    if (reps < min) tally.ShortSets++;
                    sets.Add(new { exerciseId = id, setNumber = set, weightKg = (double?)load, repetitions = (int?)reps, rir = written, completed = true, skippedReason = (string?)null, painReported = hurts && first && set == 1 });
                }
                if (athlete.Answers) feedback[id] = new { stimulus = random.NextDouble() < 0.4 ? "low" : "adequate", discomfort = "none", preFatigue = random.NextDouble() < 0.2 ? "loaded" : "recovered" };
                if (athlete.Muscles is { } mood)
                    foreach (var muscle in prescribed.GetProperty("primaryMuscles").EnumerateArray())
                        muscles[muscle.GetString()!] = mood switch
                        {
                            "fresh" => new { recovery = "recovered", workload = "low" },
                            "tired" => new { recovery = "notRecovered", workload = "high" },
                            _ => new { recovery = badDay ? "notRecovered" : "recovered", workload = "adequate" }
                        };
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
                recovery = athlete.SendsRecovery ? badDay ? "Insufficient" : "Good" : null, sessionRpe = badDay ? 9.5 : 7.5,
                painReported = hurts, sets, exerciseFeedback = feedback.Count > 0 ? feedback : null,
                muscleFeedback = muscles.Count > 0 ? muscles : null
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
        var athletes = full ? Athletes : Athletes.Where(a => a.Name is "constant" or "noisy" or "pessimist" or "wearing-down" or "too-heavy" or "sore").ToArray();
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
            // Writing the reserve low every time is a habit, not fatigue; losing performance as well is.
            var cuts = t.Applied.GetValueOrDefault("reduction_sets");
            if (athlete.Name is "pessimist" && cuts > 2)
                findings.Add(new Finding(athlete.Name, $"{goal}/{experience}", "cuts-without-fatigue", $"{cuts} set reductions"));
            if (athlete.Name is "wearing-down" && cuts == 0)
                findings.Add(new Finding(athlete.Name, $"{goal}/{experience}", "fatigue-ignored", "no set reduction while performance fell"));
            table.AppendLine($"{athlete.Name} | {goal[..3]}-{experience[..3]}-{days} | {t.Sessions} | {t.Increases} | {t.Reductions} | {t.Exercises} | {t.Underloaded} | {t.Overloaded} | {(t.Exercises == 0 ? 0 : t.Gap / t.Exercises):P0} | {string.Join(" ", t.Applied.OrderBy(a => a.Key).Select(a => $"{a.Key}={a.Value}"))}");
        }
        var report = table + "\n" + string.Join("\n", findings.GroupBy(f => f.Rule).Select(g => $"## {g.Key}: {g.Count()}\n" + string.Join("\n", g.Take(25).Select(f => $"- {f.Athlete} {f.Plan}: {f.Detail}"))));
        _output.WriteLine(report);
        if (Environment.GetEnvironmentVariable("TRAZO_SIM_REPORT") is { Length: > 0 } path) File.WriteAllText(path, report);

        Assert.True(findings.Count == 0, $"{findings.Count} findings:\n" + string.Join("\n", findings.Take(40)));
    }

    /// <summary>The adaptation half of docs/engine-scorecard.md. Measures only when <c>TRAZO_SCORECARD</c> names a report file.</summary>
    [Fact]
    public async Task AdaptationScorecard()
    {
        if (Environment.GetEnvironmentVariable("TRAZO_SCORECARD") is not { Length: > 0 } path) return;
        Athlete[] extra =
        [
            new("exaggerator", RirBias: 3),
            new("fresh", Muscles: "fresh", SendsRecovery: false),
            new("tired", Muscles: "tired", SendsRecovery: false),
            new("rough-dialog", BadDay: 0.5),
            new("rough-muscle", BadDay: 0.5, Muscles: "honest", SendsRecovery: false)
        ];
        var athletes = Athletes.Where(a => a.Name != "answers").Concat(extra).ToArray();
        (string Goal, string Experience, int Days)[] kinds = [("Hypertrophy", "Beginner", 3), ("Hypertrophy", "Intermediate", 4), ("Strength", "Intermediate", 3), ("Hypertrophy", "Advanced", 4)];
        var findings = new List<Finding>();
        var lives = new List<(string Athlete, int Kind, Tally Tally)>();
        foreach (var athlete in athletes)
        for (var kind = 0; kind < kinds.Length; kind++)
        foreach (var seed in new[] { kind + 1, kind + 101 })
            lives.Add((athlete.Name, kind, await LiveAsync(athlete, kinds[kind].Goal, kinds[kind].Experience, kinds[kind].Days, seed, findings)));

        IEnumerable<Tally> Of(params string[] names) => lives.Where(l => names.Contains(l.Athlete)).Select(l => l.Tally);
        double Share(Func<Tally, double> part, Func<Tally, double> whole, string[] names) => Of(names).Sum(part) / Math.Max(1, Of(names).Sum(whole));
        double Gap(params string[] names) => Share(t => t.Gap, t => t.Exercises, names);
        double Under(params string[] names) => Share(t => t.Underloaded, t => t.Exercises, names);
        double Over(params string[] names) => Share(t => t.Overloaded, t => t.Exercises, names);
        double Short(params string[] names) => Share(t => t.ShortSets, t => t.CompletedSets, names);
        static int Cuts(Tally t) => t.Applied.GetValueOrDefault("reduction_sets") + t.Applied.GetValueOrDefault("muscle_reduce_set");
        static int Pauses(Tally t) => t.Applied.GetValueOrDefault("hold_progression") + t.Applied.GetValueOrDefault("deload");
        int Count(string athlete, Func<Tally, bool> test) => Of(athlete).Count(test);

        string[] safetyRules = ["pain", "jump", "invalid", "http", "load", "overload"];
        var safety = findings.Count(f => safetyRules.Contains(f.Rule));
        string[] irregular = ["busy", "tires", "bad-weeks", "sore", "stubborn"];
        var irregularFindings = findings.Count(f => irregular.Contains(f.Athlete));
        var all = Of("wearing-down").Count();
        var dialogKinds = lives.Where(l => l.Athlete == "rough-dialog" && Pauses(l.Tally) > 0).Select(l => l.Kind).Distinct().ToList();
        var muscleKinds = lives.Where(l => l.Athlete == "rough-muscle" && Pauses(l.Tally) > 0).Select(l => l.Kind).Distinct().ToList();
        string[] regular = ["constant", "noisy", "stalled"];
        string[] wrong = ["optimist", "exaggerator", "pessimist"];
        var wornCut = Count("wearing-down", t => Cuts(t) > 0);
        var calm = Count("pessimist", t => Cuts(t) <= 2);
        var added = Count("fresh", t => t.Applied.GetValueOrDefault("muscle_add_set") > 0);
        var tiredCut = Count("tired", t => t.Applied.GetValueOrDefault("muscle_reduce_set") > 0);

        (string Id, bool Mandatory, string Title, bool Ok, string Result)[] checks =
        [
            ("A1", true, "Safety", safety == 0, $"{safety} findings"),
            ("A2", true, "Regular people", Gap(regular) <= 0.10 && Under(regular) + Over(regular) <= 0.05, $"gap {Gap(regular):P1}, under {Under(regular):P1}, over {Over(regular):P1}"),
            ("A3", false, "Starting too light", Gap("too-light") <= 0.12 && Under("too-light") <= 0.10, $"gap {Gap("too-light"):P1}, under {Under("too-light"):P1}"),
            ("A4", false, "Starting too heavy", Gap("too-heavy") <= 0.10 && Over("too-heavy") <= 0.05, $"gap {Gap("too-heavy"):P1}, over {Over("too-heavy"):P1}"),
            ("A5", false, "No reserve written", Gap("no-rir") <= 0.12 && Of("no-rir").All(t => t.Increases > 0), $"gap {Gap("no-rir"):P1}, plans without increases {Count("no-rir", t => t.Increases == 0)}"),
            ("A6", false, "Wrong reserve", wrong.All(n => Over(n) <= 0.05 && Short(n) <= 0.08 && Gap(n) <= 0.15),
                string.Join("; ", wrong.Select(n => $"{n}: gap {Gap(n):P1}, over {Over(n):P1}, short sets {Short(n):P1}"))),
            ("A7", false, "Novice", Gap("novice") <= 0.12, $"gap {Gap("novice"):P1}"),
            ("A8", false, "Volume follows the person", wornCut == all && calm == all && added == all && tiredCut == all,
                $"of {all} plans: wearing-down cut {wornCut}, pessimist at most 2 cuts {calm}, fresh added {added}, tired cut {tiredCut}"),
            ("A9", false, "Irregular people", irregularFindings == 0 && irregular.All(n => Gap(n) <= 0.15), $"{irregularFindings} findings; " + string.Join(", ", irregular.Select(n => $"{n} {Gap(n):P1}"))),
            ("A10", true, "Muscle questions keep the protections", dialogKinds.Count > 0 && !dialogKinds.Except(muscleKinds).Any(),
                $"plan kinds with pause or deload: dialog {dialogKinds.Count}, muscle questions {muscleKinds.Count}; changes {Of("rough-dialog").Sum(Pauses)} vs {Of("rough-muscle").Sum(Pauses)}")
        ];

        var report = new StringBuilder($"# Adaptation, engine {Trazo.Training.TrainingProgramGenerator.EngineVersion}\n\n");
        report.AppendLine($"Score: {10.0 * checks.Count(c => c.Ok) / checks.Length:0.0} ({checks.Count(c => c.Ok)}/{checks.Length}); mandatory: {(checks.Where(c => c.Mandatory).All(c => c.Ok) ? "all pass" : "FAIL")}; {lives.Count} plans lived\n");
        report.AppendLine("| | Criterion | Result | Pass |\n|---|---|---|---|");
        foreach (var c in checks) report.AppendLine($"| {c.Id}{(c.Mandatory ? " M" : "")} | {c.Title} | {c.Result} | {(c.Ok ? "yes" : "NO")} |");
        report.AppendLine("\nathlete | gap | under | over | short sets | up | down | adjustments");
        foreach (var athlete in athletes)
        {
            var n = athlete.Name;
            report.AppendLine($"{n} | {Gap(n):P1} | {Under(n):P1} | {Over(n):P1} | {Short(n):P1} | {Of(n).Sum(t => t.Increases)} | {Of(n).Sum(t => t.Reductions)} | {string.Join(" ", Of(n).SelectMany(t => t.Applied).GroupBy(a => a.Key).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Sum(a => a.Value)}"))}");
        }
        foreach (var group in findings.GroupBy(f => f.Rule))
        {
            report.AppendLine($"\n## {group.Key}: {group.Count()}");
            foreach (var f in group.Take(10)) report.AppendLine($"- {f.Athlete} {f.Plan}: {f.Detail}");
        }
        _output.WriteLine(report.ToString());
        File.WriteAllText(path + ".adaptation.md", report.ToString());
    }
}
