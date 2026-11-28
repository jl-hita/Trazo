using System.Text;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;
using M = Trazo.Training.TrainingMuscleGroup;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The generation half of docs/engine-scorecard.md measured on the real catalog. It only measures when <c>TRAZO_SCORECARD</c>
/// names a file to write the report to; the thresholds are the ones in the document.
/// </summary>
public sealed class GenerationScorecardTests(ITestOutputHelper output)
{
    private static readonly (string Name, string[] Items)[] Equipment =
    [
        ("full", []),
        ("garage", ["barbell", "bench", "squat-rack", "dumbbell", "pull-up-bar"]),
        ("db-bar", ["dumbbell", "bench", "pull-up-bar"]),
        ("db", ["dumbbell", "bench"]),
        ("bands", ["resistance-band", "pull-up-bar"])
    ];

    private sealed class Check(string id, string title, bool mandatory, double threshold)
    {
        public string Id => id;
        public string Title => title;
        public bool Mandatory => mandatory;
        public double Threshold => threshold;
        public int Total, Passed;
        public readonly Dictionary<string, int> Failures = new(StringComparer.Ordinal);
        public readonly List<string> Examples = [];
        public void Add(bool ok, string kind, Func<string> detail)
        {
            Total++;
            if (ok) { Passed++; return; }
            Failures[kind] = Failures.GetValueOrDefault(kind) + 1;
            if (Examples.Count < 12) Examples.Add(detail());
        }
        public double Rate => Total == 0 ? 0 : (double)Passed / Total;
        public bool Ok => Total > 0 && Rate >= threshold - 1e-9;
    }

    private sealed record Week(List<(string Title, int Minutes, IReadOnlyList<TrainingExercisePrescription> Exercises)> Sessions)
    {
        public int Sets => Sessions.Sum(s => s.Exercises.Sum(e => e.Sets));
        public double Reserve => Sessions.SelectMany(s => s.Exercises).Where(e => e.TargetRepetitionsInReserve is not null)
            .Select(e => (double)e.TargetRepetitionsInReserve!.Value).DefaultIfEmpty(0).Average();
        public IEnumerable<TrainingExercisePrescription> All => Sessions.SelectMany(s => s.Exercises);
        public decimal Effective(M muscle) => All.Where(e => e.StimulatedMuscles().Contains(muscle)).Sum(e => e.Sets);
        public decimal Direct(M muscle) => All.Where(e => e.PrimaryMuscleGroups.Contains(muscle)).Sum(e => e.Sets);
    }

    private static List<Week> Weeks(MesocycleGeneration block) => block.Sessions.GroupBy(s => s.WeekNumber).OrderBy(g => g.Key)
        .Select(g => new Week(g.Select(s => (s.Title, s.EstimatedDurationMinutes,
            (IReadOnlyList<TrainingExercisePrescription>)PrescriptionJson.Read(s.PrescriptionJson).OrderBy(e => e.Order).ToList())).ToList()))
        .ToList();

    [Fact]
    public void GenerationScorecard()
    {
        if (Environment.GetEnvironmentVariable("TRAZO_SCORECARD") is not { Length: > 0 } path) return;
        var catalog = RealCatalog.Candidates();
        var byId = catalog.ToDictionary(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase);
        bool Compound(string id) => byId[id].MovementPattern is not (TrainingMovementPattern.Accessory or TrainingMovementPattern.Core);

        var g1 = new Check("G1", "Integrity", true, 1);
        var g2 = new Check("G2", "A plan comes out", true, 1);
        var g3 = new Check("G3", "Blocks ramp and deload", true, 1);
        var g4 = new Check("G4", "Weekly effective dose 8-22", false, .95);
        var g5 = new Check("G5", "Direct work >= 4", false, .90);
        var g6 = new Check("G6", "Twice a week", false, .95);
        var g7 = new Check("G7", "<= 12 sets per muscle and session", false, .99);
        var g8 = new Check("G8", "Repetition range spread", false, .90);
        var g9 = new Check("G9", "Continuity between blocks", false, .90);
        var g10 = new Check("G10", "Push/pull and quad/ham balance", false, .95);
        var g11 = new Check("G11", "Time used", false, .85);
        var g12 = new Check("G12", "Strength main lift", false, .90);
        var generated = new Dictionary<string, (int Ok, int All)>();

        string[][] limitationSets = [[], ["Shoulder"], ["Knee", "LowerBack"]];
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4, 5, 6 })
        foreach (var minutes in new[] { 45, 60, 90 })
        foreach (var (equipment, items) in Equipment)
        foreach (var seed in new[] { 7, 123 })
        {
            var gym = equipment is "full" or "garage";
            // Limitations and an exclusion are checked on one seed of the 60-minute gym profiles.
            var limitations = seed == 123 && minutes == 60 ? limitationSets[days % 3] : [];
            var excluded = limitations.Length > 0 ? new[] { "bench-press" } : [];
            var name = $"{goal}/{experience}/{days}d/{minutes}m/{equipment}/s{seed}";
            // As the app asks for it: "Automatic" covers 2-4 days, so with more it sends push, pull and legs.
            var profile = PlanTestCatalog.Profile(goal, experience, days, items, excluded, limitations, minutes: minutes,
                split: days > 4 ? TrainingProgramSplit.PushPullLegs : TrainingProgramSplit.Automatic);

            var first = MesocycleGenerator.Generate(profile, seed, new MesocycleSlot(1, 1, "accumulation", 1, 4), catalog);
            var minimal = equipment is "db" or "bands";
            var tally = generated.GetValueOrDefault(minimal ? "small" : "equipped");
            generated[minimal ? "small" : "equipped"] = (tally.Ok + (first.Succeeded ? 1 : 0), tally.All + 1);
            if (!first.Succeeded) { g2.Failures[name] = 1; if (g2.Examples.Count < 12) g2.Examples.Add($"{name}: {string.Join("; ", first.Errors)}"); continue; }
            var used = first.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).Distinct().ToList();
            var deload = MesocycleGenerator.Generate(profile, seed, new MesocycleSlot(2, 2, "deload", 5, 1), catalog, used);
            var deloadUsed = deload.Succeeded ? deload.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).Distinct().ToList() : used;
            var second = MesocycleGenerator.Generate(profile, seed, new MesocycleSlot(3, 3, "accumulation", 6, 4), catalog, deloadUsed);
            if (!deload.Succeeded || !second.Succeeded) { g1.Add(false, "later-block", () => $"{name}: {string.Join("; ", deload.Errors.Concat(second.Errors))}"); continue; }

            var forbidden = LimitationRules.ExcludedExerciseIds(limitations, catalog).Concat(excluded).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = items.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var block in new[] { first, deload, second })
            foreach (var week in Weeks(block))
            foreach (var session in week.Sessions)
            {
                g1.Add(session.Minutes <= minutes, "time", () => $"{name}: {session.Title} takes {session.Minutes} min");
                g1.Add(session.Exercises.Select(e => e.ExerciseId).Distinct().Count() == session.Exercises.Count, "duplicate", () => $"{name}: {session.Title}");
                foreach (var exercise in session.Exercises)
                {
                    var c = byId[exercise.ExerciseId];
                    var fits = available.Count == 0 || c.RequiredEquipment.All(t => available.Contains(t) || (t == "machine" && available.Any(a => a.EndsWith("-machine"))));
                    g1.Add(fits && !forbidden.Contains(exercise.ExerciseId) && (int)c.MinimumExperience <= (int)experience, "exercise",
                        () => $"{name}: {exercise.ExerciseId}");
                }
                if (block != deload)
                {
                    var worst = Enum.GetValues<M>().Select(m => (m, Sets: session.Exercises.Where(e => e.StimulatedMuscles().Contains(m)).Sum(e => e.Sets))).MaxBy(x => x.Sets);
                    g7.Add(worst.Sets <= 12, worst.m.ToString(), () => $"{name}: {session.Title} {worst.m} {worst.Sets}");
                }
            }

            var training = new[] { Weeks(first), Weeks(second) };
            var peak = training[0][^1];
            if (goal != TrainingGoal.GeneralFitness)
                foreach (var weeks in training)
                {
                    var ok = true;
                    for (var i = 1; i < weeks.Count; i++) ok &= weeks[i].Sets >= weeks[i - 1].Sets && weeks[i].Reserve <= weeks[i - 1].Reserve + 1e-9;
                    ok &= Weeks(deload)[0].Sets <= 0.70 * weeks[^1].Sets;
                    g3.Add(ok, goal.ToString(), () => $"{name}: sets {string.Join(",", weeks.Select(w => w.Sets))}, deload {Weeks(deload)[0].Sets}, rir {string.Join(",", weeks.Select(w => w.Reserve.ToString("0.0")))}");
                }

            // Back: the better of lats and upper back, which the same pulls share.
            (string Name, M[] Muscles)[] majors =
            [
                ("Chest", [M.Chest]), ("Back", [M.Lats, M.UpperBack]), ("Quadriceps", [M.Quadriceps]), ("Hamstrings", [M.Hamstrings]),
                ("Glutes", [M.Glutes]), ("Deltoids", [M.Deltoids]), ("Biceps", [M.Biceps]), ("Triceps", [M.Triceps]), ("Calves", [M.Calves])
            ];
            var hyp = goal == TrainingGoal.Hypertrophy;
            var experienced = experience != TrainingExperience.Beginner;
            if (hyp && experienced && days >= 3 && minutes >= 60 && gym && limitations.Length == 0)
                foreach (var (muscle, parts) in majors)
                {
                    if (muscle != "Calves")
                    {
                        var sets = parts.Max(peak.Effective);
                        g4.Add(sets is >= 8 and <= 22, $"{muscle}{(sets < 8 ? " low" : " high")}", () => $"{name}: {muscle} {sets}");
                    }
                    if (muscle != "Glutes")
                    {
                        var direct = parts.Max(peak.Direct);
                        g5.Add(direct >= 4, muscle, () => $"{name}: {muscle} direct {direct}");
                    }
                }
            if (hyp && minutes >= 60 && gym && limitations.Length == 0)
                foreach (var (muscle, parts) in majors.Where(m => m.Name != "Calves"))
                {
                    var times = parts.Max(p => peak.Sessions.Count(s => s.Exercises.Any(e => e.StimulatedMuscles().Contains(p))));
                    g6.Add(times >= 2, muscle, () => $"{name}: {muscle} in {times} session(s)");
                }
            if (hyp && experienced && minutes >= 60 && gym)
            {
                var ranges = peak.All.Select(e => (e.MinRepetitions, e.MaxRepetitions)).Distinct().ToList();
                g8.Add(ranges.Count >= 3 && ranges.Any(r => r.MaxRepetitions <= 10) && ranges.Any(r => r.MaxRepetitions >= 15), "ranges",
                    () => $"{name}: {string.Join(" ", ranges.Select(r => $"{r.MinRepetitions}-{r.MaxRepetitions}"))}");
            }
            if (gym)
            {
                var before = training[0][^1].All.Select(e => e.ExerciseId).Distinct().ToHashSet();
                var after = training[1][^1].All.Select(e => e.ExerciseId).Distinct().ToHashSet();
                var compounds = before.Where(Compound).ToList();
                var kept = compounds.Count == 0 ? 1 : (double)compounds.Count(after.Contains) / compounds.Count;
                var changed = !before.SetEquals(after);
                g9.Add(kept >= 0.60 && changed, kept < 0.60 ? "lifts replaced" : "nothing changes", () => $"{name}: keeps {kept:P0}");
            }
            if (equipment is not ("db" or "bands") && limitations.Length == 0)
            {
                decimal Pattern(params TrainingMovementPattern[] patterns) => peak.All.Where(e => patterns.Contains(byId[e.ExerciseId].MovementPattern)).Sum(e => e.Sets);
                var push = Pattern(TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.VerticalPush);
                var pull = Pattern(TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPull);
                var quads = peak.Effective(M.Quadriceps);
                var hams = peak.Effective(M.Hamstrings);
                var pulls = pull >= 0.8m * push;
                g10.Add(pulls && hams >= 0.5m * quads, pulls ? "hamstrings" : "pull", () => $"{name}: push {push} pull {pull} quads {quads} hams {hams}");
            }
            if (hyp && minutes <= 60 && gym)
                foreach (var session in peak.Sessions)
                    g11.Add(session.Minutes >= 0.65 * minutes, $"{minutes}m", () => $"{name}: {session.Title} {session.Minutes} min");
            if (goal == TrainingGoal.Strength && gym)
            {
                var nextOpeners = training[1][^1].Sessions.Select(s => s.Exercises[0].ExerciseId).ToList();
                for (var i = 0; i < peak.Sessions.Count; i++)
                {
                    var main = peak.Sessions[i].Exercises[0];
                    var ok = Compound(main.ExerciseId) && main.MaxRepetitions <= 6 && main.Sets >= 3 && nextOpeners.Contains(main.ExerciseId);
                    g12.Add(ok, experience.ToString(), () => $"{name}: {main.ExerciseId} {main.Sets}x{main.MinRepetitions}-{main.MaxRepetitions}");
                }
            }
        }

        // G2 has its own two thresholds.
        var equipped = generated.GetValueOrDefault("equipped");
        var small = generated.GetValueOrDefault("small");
        var availability = (double)equipped.Ok / equipped.All >= 0.99 && (double)small.Ok / Math.Max(1, small.All) >= 0.90;
        g2.Total = 1;
        g2.Passed = availability ? 1 : 0;

        var checks = new[] { g1, g2, g3, g4, g5, g6, g7, g8, g9, g10, g11, g12 };
        var report = new StringBuilder($"# Generation, engine {TrainingProgramGenerator.EngineVersion}\n\n");
        var passed = checks.Count(c => c.Ok);
        report.AppendLine($"Score: {10.0 * passed / checks.Length:0.0} ({passed}/{checks.Length}); mandatory: {(checks.Where(c => c.Mandatory).All(c => c.Ok) ? "all pass" : "FAIL")}");
        report.AppendLine($"Plans generated: equipped {equipped.Ok}/{equipped.All}, small sets {small.Ok}/{small.All}\n");
        report.AppendLine("| | Criterion | Result | Needs | Pass |\n|---|---|---|---|---|");
        foreach (var c in checks)
            report.AppendLine($"| {c.Id}{(c.Mandatory ? " M" : "")} | {c.Title} | {(c.Id == "G2" ? "see above" : $"{c.Rate:P1} ({c.Total - c.Passed} of {c.Total} fail)")} | {(c.Id == "G2" ? "99 % / 90 %" : $"{c.Threshold:P0}")} | {(c.Ok ? "yes" : "NO")} |");
        foreach (var c in checks.Where(c => c.Failures.Count > 0))
        {
            report.AppendLine($"\n## {c.Id}: {string.Join(", ", c.Failures.OrderByDescending(f => f.Value).Take(15).Select(f => $"{f.Key} {f.Value}"))}");
            foreach (var example in c.Examples) report.AppendLine($"- {example}");
        }
        output.WriteLine(report.ToString());
        File.WriteAllText(path + ".generation.md", report.ToString());
    }
}
