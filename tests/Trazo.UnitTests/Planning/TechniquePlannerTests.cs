using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>The generator's techniques over the real catalog: who gets them, why, and the limits that are never crossed.</summary>
public sealed class TechniquePlannerTests(ITestOutputHelper output)
{
    private static readonly string[] Gym = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];
    private static readonly MesocycleSlot Block = new(1, 1, "accumulation", 1, 4);

    private static PlanProfile Profile(
        TrainingExperience experience = TrainingExperience.Intermediate, int days = 4, int minutes = 60,
        TrainingGoal goal = TrainingGoal.Hypertrophy, params TrainingMuscleGroup[] emphasised) =>
        PlanTestCatalog.Profile(goal, experience, days, Gym, null, null, 12, minutes, TrainingProgramSplit.Automatic) with
        {
            MusclePriorities = emphasised.ToDictionary(m => m, _ => TrainingMusclePriority.Emphasize)
        };

    private static MesocycleGeneration Generate(PlanProfile profile, MesocycleSlot? slot = null, TechniqueAllowance? allowance = null,
        int seed = 7) =>
        MesocycleGenerator.Generate(profile, seed, slot ?? Block, RealCatalog.Candidates(),
            techniques: allowance ?? TechniqueAllowance.NoPain);

    private static IEnumerable<(GeneratedSession Session, TrainingExercisePrescription Exercise)> Techniques(MesocycleGeneration generation) =>
        generation.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson).Where(e => e.Technique is not null).Select(e => (s, e)));

    [Fact]
    public void WithoutAnAppThatShowsThem_TheBlockHasNone()
    {
        var profile = Profile(emphasised: TrainingMuscleGroup.Biceps);
        var plain = MesocycleGenerator.Generate(profile, 7, Block, RealCatalog.Candidates());
        Assert.True(plain.Succeeded);
        Assert.All(plain.Sessions, s => Assert.False(PrescriptionJson.HasTechniques(s.PrescriptionJson)));
    }

    [Fact]
    public void AnEmphasisedMuscleGetsOneTechniqueAWeek_AfterTheIntroductoryWeek()
    {
        var generation = Generate(Profile(emphasised: TrainingMuscleGroup.Deltoids));
        var priority = Techniques(generation).Where(t => t.Exercise.Technique!.Reason == TechniqueReasons.PriorityMuscle).ToList();
        Assert.NotEmpty(priority);
        Assert.DoesNotContain(priority, t => t.Session.WeekNumber == 1);
        Assert.All(priority, t => Assert.Contains(TrainingMuscleGroup.Deltoids, t.Exercise.PrimaryMuscleGroups));
        Assert.All(priority.GroupBy(t => t.Session.WeekNumber), week => Assert.Single(week));
        Assert.All(priority, t => Assert.Equal(TrainingTechniqueType.DropSet, t.Exercise.Technique!.Type));
    }

    [Theory]
    [InlineData(TrainingExperience.Beginner, TrainingGoal.Hypertrophy)]
    [InlineData(TrainingExperience.Intermediate, TrainingGoal.Strength)]
    [InlineData(TrainingExperience.Advanced, TrainingGoal.GeneralFitness)]
    public void BeginnersAndOtherGoalsGetNone(TrainingExperience experience, TrainingGoal goal)
    {
        var generation = Generate(Profile(experience, minutes: 45, goal: goal));
        Assert.True(generation.Succeeded);
        Assert.All(generation.Sessions, s => Assert.False(PrescriptionJson.HasTechniques(s.PrescriptionJson)));
    }

    [Fact]
    public void DeloadAndPreparationBlocksGetNone()
    {
        var profile = Profile(TrainingExperience.Advanced, minutes: 45, emphasised: TrainingMuscleGroup.Biceps);
        foreach (var slot in new[] { new MesocycleSlot(2, 2, "deload", 5, 1), new MesocycleSlot(1, 1, PreparationPhase.PhaseType, 1, 3) })
            Assert.All(Generate(profile, slot).Sessions, s => Assert.False(PrescriptionJson.HasTechniques(s.PrescriptionJson)));
    }

    [Fact]
    public void AnExerciseWithPainGetsNone()
    {
        var profile = Profile(emphasised: TrainingMuscleGroup.Deltoids);
        var painful = Techniques(Generate(profile)).Select(t => t.Exercise.ExerciseId).Distinct().ToArray();
        Assert.NotEmpty(painful);
        var again = Generate(profile, allowance: new TechniqueAllowance(painful));
        Assert.DoesNotContain(Techniques(again), t => painful.Contains(t.Exercise.ExerciseId));
    }

    [Fact]
    public void EveryGeneratedPlanKeepsTheLimits()
    {
        var catalog = RealCatalog.Candidates().ToDictionary(c => c.ExerciseId);
        int plans = 0, techniques = 0, supersets = 0;
        var byReason = new SortedDictionary<string, int>();
        foreach (var experience in new[] { TrainingExperience.Intermediate, TrainingExperience.Advanced })
        foreach (var days in new[] { 2, 3, 4 })
        foreach (var minutes in new[] { 35, 45, 60, 90 })
        foreach (var emphasised in new TrainingMuscleGroup[][] { [], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Chest] })
        foreach (var seed in new[] { 7, 11 })
        {
            var profile = Profile(experience, days, minutes, emphasised: emphasised);
            var generation = Generate(profile, seed: seed);
            var plain = MesocycleGenerator.Generate(profile, seed, Block, RealCatalog.Candidates());
            Assert.Equal(plain.Succeeded, generation.Succeeded);
            if (!generation.Succeeded) continue;
            plans++;
            for (var i = 0; i < generation.Sessions.Count; i++)
            {
                var session = generation.Sessions[i];
                var exercises = PrescriptionJson.Read(session.PrescriptionJson);
                var before = PrescriptionJson.Read(plain.Sessions[i].PrescriptionJson);
                var groups = exercises.ToDictionary(e => e.ExerciseId, e => PrescriptionJson.SupersetGroup(session.PrescriptionJson, e.ExerciseId));
                var rounds = PrescriptionJson.Supersets(session.PrescriptionJson);
                var with = exercises.Where(e => e.Technique is not null).ToList();
                techniques += with.Count;
                supersets += rounds.Count;

                // The same exercises; only a superset may add one set to one of them.
                Assert.Equal(before.Select(e => e.ExerciseId).Order(), exercises.Select(e => e.ExerciseId).Order());
                var added = exercises.Sum(e => e.Sets) - before.Sum(e => e.Sets);
                Assert.Equal(rounds.Count, added);
                // A session the engine already planned inside the time stays inside it.
                Assert.True(session.EstimatedDurationMinutes <= minutes || plain.Sessions[i].EstimatedDurationMinutes > minutes,
                    $"{session.EstimatedDurationMinutes} > {minutes}");
                Assert.True(with.Count <= TechniquePlanner.MaximumTechniquesPerSession);
                foreach (var exercise in with)
                {
                    var candidate = catalog[exercise.ExerciseId];
                    Assert.Equal(TrainingMovementPattern.Accessory, candidate.MovementPattern);
                    Assert.Empty(TechniqueRules.Validate(exercise.Technique!, exercise.Sets, candidate.AllowedTechniques, groups[exercise.ExerciseId] is not null));
                    var reason = exercise.Technique!.Reason!;
                    byReason[reason] = byReason.GetValueOrDefault(reason) + 1;
                    if (experience == TrainingExperience.Intermediate) Assert.Equal(TrainingTechniqueType.DropSet, exercise.Technique.Type);
                }
                foreach (var superset in rounds)
                {
                    Assert.Empty(TechniqueRules.Validate(superset));
                    var members = exercises.Where(e => groups[e.ExerciseId] == superset.Group).OrderBy(e => e.Order).ToList();
                    Assert.Equal(2, members.Count);
                    Assert.Equal(members[0].Order + 1, members[1].Order);
                    Assert.All(members, m => Assert.Equal(TrainingMovementPattern.Accessory, catalog[m.ExerciseId].MovementPattern));
                    Assert.False(members[0].StimulatedMuscles().Overlaps(members[1].StimulatedMuscles()));
                }
            }
            foreach (var week in Techniques(generation).GroupBy(t => t.Session.WeekNumber))
                Assert.All(week.SelectMany(t => t.Exercise.PrimaryMuscleGroups).GroupBy(m => m), muscle => Assert.Single(muscle));
        }
        output.WriteLine($"{plans} plans, {techniques} techniques ({string.Join(", ", byReason)}), {supersets} supersets");
        Assert.True(techniques > 0);
    }
}
