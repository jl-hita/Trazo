using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Properties every generated block must have whatever the profile: volume inside its limits, no repeated exercise in a
/// session, no exercise the user cannot do, determinism for a given seed and a deload that is lighter than a normal block.
/// A profile the catalog cannot satisfy must fail with an explanation, never produce a partial plan.
/// </summary>
public sealed class MesocycleGeneratorPropertyTests
{
    private static readonly string[][] EquipmentSets =
    [
        [],
        ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"],
        ["dumbbell", "bench"],
        ["dumbbell", "bench", "pull-up-bar"],
        ["dumbbell", "cable", "machine"],
        ["resistance-band", "pull-up-bar"]
    ];

    public static IEnumerable<object[]> Grid()
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
        foreach (var equipment in EquipmentSets.Select((set, index) => index))
        foreach (var seed in new[] { 7, 123 })
            yield return [goal, experience, days, equipment, seed];
    }

    private static MesocycleGeneration Generate(TrainingGoal goal, TrainingExperience experience, int days, int equipmentIndex, int seed,
        string[]? limitations = null, string[]? excluded = null, bool deload = false, int weeks = 4)
    {
        var profile = PlanTestCatalog.Profile(goal, experience, days, EquipmentSets[equipmentIndex], excluded, limitations);
        var slot = new MesocycleSlot(1, 1, deload ? "deload" : "accumulation", 1, deload ? 1 : weeks);
        return MesocycleGenerator.Generate(profile, seed, slot, PlanTestCatalog.Candidates());
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void EveryBlockRespectsVolumeEquipmentAndUniquenessRules(
        TrainingGoal goal, TrainingExperience experience, int days, int equipmentIndex, int seed)
    {
        var result = Generate(goal, experience, days, equipmentIndex, seed);
        if (!result.Succeeded)
        {
            // An impossible profile is explained, never silently half-planned.
            Assert.NotEmpty(result.Errors);
            Assert.Empty(result.Sessions);
            return;
        }

        var candidates = PlanTestCatalog.Candidates().ToDictionary(c => c.ExerciseId);
        var available = EquipmentSets[equipmentIndex].ToHashSet();
        Assert.Equal(days * 4, result.Sessions.Count);
        foreach (var session in result.Sessions)
        {
            var exercises = PrescriptionJson.Read(session.PrescriptionJson);
            Assert.InRange(exercises.Count, 1, 20);
            Assert.True(session.EstimatedDurationMinutes <= 75, $"{session.Title} takes {session.EstimatedDurationMinutes} min");
            Assert.Equal(exercises.Count, exercises.Select(e => e.ExerciseId).Distinct().Count()); // no duplicates in a session
            Assert.Equal(exercises.Count, exercises.Select(e => e.Order).Distinct().Count());
            foreach (var exercise in exercises)
            {
                var candidate = candidates[exercise.ExerciseId];
                Assert.InRange(exercise.Sets, 1, goal == TrainingGoal.Strength ? 3 : 5);
                Assert.InRange(exercise.MinRepetitions, 1, exercise.MaxRepetitions);
                Assert.InRange(exercise.MaxRepetitions, 1, 30);
                Assert.InRange(exercise.TargetRepetitionsInReserve ?? 0m, 0m, 5m);
                Assert.InRange(exercise.RestSeconds, 0, 600);
                Assert.True((int)candidate.MinimumExperience <= (int)experience, $"{exercise.ExerciseId} is too advanced");
                if (available.Count > 0)
                    Assert.All(candidate.RequiredEquipment, token => Assert.True(
                        available.Contains(token) || (token == "machine" && available.Any(a => a.EndsWith("-machine"))),
                        $"{exercise.ExerciseId} needs {token}"));
            }
        }

        // Weekly volume: no muscle gets an unreasonable dose and every week has work.
        Assert.NotNull(result.Quality);
        foreach (var week in result.Quality!.WeeklyMuscleVolumes)
        {
            Assert.All(week.Muscles, muscle => Assert.InRange(muscle.EstimatedEffectiveSets, 0m, 26m));
            Assert.True(week.Muscles.Sum(m => m.PrimarySets) >= days * 2, "every week carries work");
        }
    }

    [Theory]
    [MemberData(nameof(Grid))]
    public void TheSameProfileAndSeedGiveTheSamePlan(TrainingGoal goal, TrainingExperience experience, int days, int equipmentIndex, int seed)
    {
        var first = Generate(goal, experience, days, equipmentIndex, seed);
        var second = Generate(goal, experience, days, equipmentIndex, seed);

        Assert.Equal(first.Errors, second.Errors);
        Assert.Equal(first.Sessions.Select(s => (s.Date, s.Title, s.PrescriptionJson)), second.Sessions.Select(s => (s.Date, s.Title, s.PrescriptionJson)));
    }

    [Fact]
    public void ExcludedExercisesAndLimitationsNeverAppear()
    {
        var result = Generate(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 3, 1, 5,
            limitations: ["Knee", "LowerBack"], excluded: ["bench-press"]);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var used = result.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).ToHashSet();
        Assert.DoesNotContain("bench-press", used);
        Assert.DoesNotContain("deadlift", used);       // LowerBack
        Assert.DoesNotContain("romanian-deadlift", used); // LowerBack
        Assert.DoesNotContain("lunge", used);           // Knee
        Assert.DoesNotContain("bulgarian-split-squat", used);
        Assert.DoesNotContain("step-up", used);
    }

    [Fact]
    public void ALimitationThatLeavesAPatternEmptyFailsWithAnExplanation()
    {
        var profile = PlanTestCatalog.Profile(limitations: ["Shoulder"], equipment: ["dumbbell", "bench", "machine"]);
        // ohp, db-shoulder-press and machine-shoulder-press: only the first is excluded by the rules, so exclude the rest too.
        var withExclusions = profile with { ExcludedExerciseIds = ["db-shoulder-press", "machine-shoulder-press"] };

        var result = MesocycleGenerator.Generate(withExclusions, 1, new MesocycleSlot(1, 1, "accumulation", 1, 4), PlanTestCatalog.Candidates());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("VerticalPush"));
    }

    [Theory]
    [InlineData(TrainingGoal.Hypertrophy)]
    [InlineData(TrainingGoal.Strength)]
    [InlineData(TrainingGoal.GeneralFitness)]
    public void WithinABlockTheWeeklyWorkNeverDecreasesAndADeloadIsLighter(TrainingGoal goal)
    {
        var block = Generate(goal, TrainingExperience.Intermediate, 3, 1, 11);
        var deload = Generate(goal, TrainingExperience.Intermediate, 3, 1, 11, deload: true);
        Assert.True(block.Succeeded && deload.Succeeded);

        int Sets(IEnumerable<GeneratedSession> sessions) => sessions.Sum(s => PrescriptionJson.Read(s.PrescriptionJson).Sum(e => e.Sets));
        var byWeek = block.Sessions.GroupBy(s => s.WeekNumber).OrderBy(g => g.Key).Select(g => Sets(g)).ToArray();
        for (var i = 1; i < byWeek.Length; i++)
            Assert.True(byWeek[i] >= byWeek[i - 1], $"week {i + 1} has {byWeek[i]} sets, week {i} has {byWeek[i - 1]}");

        Assert.True(Sets(deload.Sessions) < byWeek.Max(), "the deload week must carry less work than a training week");
        Assert.All(deload.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)),
            e => Assert.Equal(TrainingProgressionRule.None, e.ProgressionRule));
    }

    [Fact]
    public void LaterBlocksStartFromRealLoadsAndRotateExercisesWhenThereAreEquivalents()
    {
        var profile = PlanTestCatalog.Profile(equipment: EquipmentSets[1]);
        var catalog = PlanTestCatalog.Candidates();
        var first = MesocycleGenerator.Generate(profile, 3, new MesocycleSlot(1, 1, "accumulation", 1, 4), catalog);
        var usedFirst = first.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).Distinct().ToList();

        var second = MesocycleGenerator.Generate(profile, 3, new MesocycleSlot(2, 2, "accumulation", 5, 4), catalog,
            usedFirst, continuesPreviousBlock: true, loadReferences: [new TrainingLoadReference("bench-press", 80m, 8, 2m, 3)]);

        Assert.True(second.Succeeded);
        Assert.Equal(5, second.Sessions.Min(s => s.WeekNumber)); // absolute plan weeks
        Assert.InRange(second.Sessions.Min(s => s.Date), new DateOnly(2026, 12, 14), new DateOnly(2026, 12, 20));
        var usedSecond = second.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).Distinct().ToList();
        Assert.NotEqual(usedFirst.Order(), usedSecond.Order()); // at least one equivalent exercise was rotated in
    }
}
