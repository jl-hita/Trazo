using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Properties a personal trainer would check in a generated plan, beyond the structural invariants of
/// <see cref="MesocycleGeneratorPropertyTests"/>: limitations that really exclude and never break the plan, strength
/// sessions built around a heavy main lift that stays from block to block, a selection that does not regress the
/// user or favour the alphabet, balanced lower-body volume, core work and a reasonable use of the time offered.
/// They pin the corrections of engine 0.10.0 (docs/planning-engine.md, section 4).
/// </summary>
public sealed class EngineCoachingQualityTests
{
    private static readonly string[] FullGym = [];
    private static readonly string[] HomeDumbbells = ["dumbbell", "bench"];
    private static readonly string[] Garage = ["barbell", "bench", "squat-rack", "dumbbell", "pull-up-bar"];

    private static readonly IReadOnlyDictionary<string, TrainingExerciseCandidate> Catalog =
        PlanTestCatalog.Candidates().ToDictionary(candidate => candidate.ExerciseId);

    private static MesocycleGeneration Block(PlanProfile profile, int seed = 7, int sequence = 1,
        IReadOnlyList<string>? previous = null, bool deload = false) =>
        MesocycleGenerator.Generate(profile, seed,
            new MesocycleSlot(sequence, sequence, deload ? "deload" : "accumulation", 1 + 4 * (sequence - 1), deload ? 1 : 4),
            PlanTestCatalog.Candidates(), previous, continuesPreviousBlock: previous is not null && !deload);

    /// <summary>The sessions of the block's third week (the allocated dose, after the introductory weeks), in date order.</summary>
    private static List<List<TrainingExercisePrescription>> Week(MesocycleGeneration block, int weekInBlock = 3)
    {
        var first = block.Sessions.Min(session => session.WeekNumber);
        var week = Math.Min(first + weekInBlock - 1, block.Sessions.Max(session => session.WeekNumber));
        return block.Sessions.Where(session => session.WeekNumber == week).OrderBy(session => session.Date)
            .Select(session => PrescriptionJson.Read(session.PrescriptionJson).OrderBy(exercise => exercise.Order).ToList())
            .ToList();
    }

    private static HashSet<string> Used(MesocycleGeneration block) =>
        block.Sessions.SelectMany(session => PrescriptionJson.Read(session.PrescriptionJson))
            .Select(exercise => exercise.ExerciseId).ToHashSet();

    private static TrainingMovementPattern Pattern(TrainingExercisePrescription exercise) => Catalog[exercise.ExerciseId].MovementPattern;

    private static int DirectSets(IEnumerable<TrainingExercisePrescription> exercises, TrainingMuscleGroup muscle) =>
        exercises.Where(exercise => exercise.PrimaryMuscleGroups.Contains(muscle)).Sum(exercise => exercise.Sets);

    // ---- 1. Limitations ----------------------------------------------------------------------------------------

    public static IEnumerable<object[]> LimitationSets()
    {
        var areas = LimitationRules.Areas;
        foreach (var area in areas) yield return [new[] { area }];
        for (var i = 0; i < areas.Count; i++)
        for (var j = i + 1; j < areas.Count; j++)
            yield return [new[] { areas[i], areas[j] }];
    }

    [Theory]
    [MemberData(nameof(LimitationSets))]
    public void NoLimitationOrPairOfLimitationsBreaksAPlanInAFullGym(string[] limitations)
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
        {
            var profile = PlanTestCatalog.Profile(goal, experience, days, FullGym, limitations: limitations, minutes: 60);
            var block = Block(profile);

            Assert.True(block.Succeeded, $"{string.Join("+", limitations)} {goal} {experience} {days}d: {string.Join("; ", block.Errors)}");
            var excluded = LimitationRules.ExcludedExerciseIds(limitations, PlanTestCatalog.Candidates());
            Assert.Empty(Used(block).Intersect(excluded));
        }
    }

    [Fact]
    public void AKneeLimitationRemovesLoadedSingleLegWorkAndThePlanKeepsItsHingeInstead()
    {
        // Without machines the only single-leg exercises are lunges, split squats and step-ups: all excluded.
        var profile = PlanTestCatalog.Profile(days: 4, equipment: Garage, limitations: ["Knee"]);
        var block = Block(profile);

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        var used = Used(block);
        Assert.Empty(used.Intersect(["lunge", "bulgarian-split-squat", "step-up"]));
        Assert.DoesNotContain(used, id => Catalog[id].MovementPattern == TrainingMovementPattern.SingleLeg);
        // Every lower-body session still trains squat and hinge, and the omission is explained.
        var lower = Week(block).Where(session => session.Any(exercise => Pattern(exercise) == TrainingMovementPattern.Squat)).ToList();
        Assert.NotEmpty(lower);
        Assert.All(lower, session => Assert.Contains(session, exercise => Pattern(exercise) == TrainingMovementPattern.Hinge));
        Assert.Contains(block.Quality!.Warnings, warning => warning.Contains("SingleLeg", StringComparison.Ordinal));
    }

    [Fact]
    public void AKneeLimitationInAFullBodyWeekReplacesSingleLegWorkWithAHinge()
    {
        // "Cuerpo completo B" has no hinge of its own: the single-leg slot becomes one instead of disappearing.
        var profile = PlanTestCatalog.Profile(days: 2, equipment: Garage, limitations: ["Knee"]);
        var block = Block(profile);

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        Assert.All(Week(block), session => Assert.Contains(session, exercise => Pattern(exercise) == TrainingMovementPattern.Hinge));
    }

    [Theory]
    [InlineData(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 3)]
    [InlineData(TrainingGoal.Strength, TrainingExperience.Advanced, 3)]
    public void AShoulderLimitationRemovesEveryOverheadPressAndDirectDeltoidRaise(TrainingGoal goal, TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, limitations: ["Shoulder"]));

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        var used = Used(block);
        Assert.DoesNotContain(used, id => Catalog[id].MovementPattern == TrainingMovementPattern.VerticalPush);
        Assert.Empty(used.Intersect(["ohp", "db-shoulder-press", "machine-shoulder-press", "lateral-raise", "overhead-tricep-extension"]));
        // Horizontal pressing and rear-deltoid work stay available.
        Assert.Contains(used, id => Catalog[id].MovementPattern == TrainingMovementPattern.HorizontalPush);
    }

    [Fact]
    public void WristElbowAndLowerBackRulesExcludeTheExercisesThatLoadTheArea()
    {
        var catalog = PlanTestCatalog.Candidates();
        var wrist = LimitationRules.ExcludedExerciseIds(["Wrist"], catalog);
        var elbow = LimitationRules.ExcludedExerciseIds(["Elbow"], catalog);
        var lowerBack = LimitationRules.ExcludedExerciseIds(["lower back"], catalog);

        Assert.Superset(new HashSet<string> { "push-up", "knee-push-up", "plank", "barbell-curl" }, wrist.ToHashSet());
        Assert.DoesNotContain("db-bench-press", wrist);
        Assert.Superset(new HashSet<string> { "overhead-tricep-extension", "barbell-curl" }, elbow.ToHashSet());
        Assert.DoesNotContain("tricep-pushdown", elbow);
        Assert.Superset(new HashSet<string> { "deadlift", "romanian-deadlift", "rack-pull", "barbell-row", "squat" }, lowerBack.ToHashSet());
        Assert.DoesNotContain("leg-press", lowerBack);
        Assert.DoesNotContain("hip-thrust", lowerBack);
    }

    [Fact]
    public void AProfileLeftWithoutAnEssentialPatternStillFailsWithAnExplanation()
    {
        // Bodyweight only plus a wrist limitation: no horizontal press is left, and that pattern is never dropped.
        var profile = PlanTestCatalog.Profile(equipment: ["pull-up-bar"], limitations: ["Wrist"]);

        var block = Block(profile);

        Assert.False(block.Succeeded);
        Assert.Empty(block.Sessions);
        Assert.Contains(block.Errors, error => error.Contains("HorizontalPush", StringComparison.Ordinal));
    }

    // ---- 2. Strength -------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> StrengthProfiles()
    {
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
        foreach (var seed in new[] { 3, 7, 123 })
            yield return [experience, days, seed];
    }

    [Theory]
    [MemberData(nameof(StrengthProfiles))]
    public void EveryStrengthSessionOpensWithAHeavyCompoundOfThreeToFiveSets(TrainingExperience experience, int days, int seed)
    {
        var block = Block(PlanTestCatalog.Profile(TrainingGoal.Strength, experience, days, FullGym), seed);

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        foreach (var week in new[] { 1, 3 })
        foreach (var session in Week(block, week))
        {
            var main = session[0];
            var candidate = Catalog[main.ExerciseId];
            Assert.Contains(candidate.MovementPattern, new[]
            {
                TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.HorizontalPush,
                TrainingMovementPattern.VerticalPush, TrainingMovementPattern.HorizontalPull
            });
            // A barbell or a machine: never a goblet squat, a pulldown, a dumbbell squat or a push-up.
            Assert.Contains(candidate.RequiredEquipment, token => token == "barbell" || token.EndsWith("-machine", StringComparison.OrdinalIgnoreCase));
            Assert.InRange(main.Sets, 3, 5);
            if (experience != TrainingExperience.Beginner)
                Assert.Equal((3, 6), (main.MinRepetitions, main.MaxRepetitions));
            Assert.All(session.Skip(1), exercise => Assert.InRange(exercise.Sets, 1, 3));
        }
    }

    [Fact]
    public void AStrengthWeekDoesNotOpenEverySessionWithTheSamePattern()
    {
        var block = Block(PlanTestCatalog.Profile(TrainingGoal.Strength, TrainingExperience.Intermediate, 3, FullGym));

        var openers = Week(block).Select(session => Pattern(session[0])).ToList();
        Assert.Equal(3, openers.Distinct().Count());
        Assert.Contains(TrainingMovementPattern.Squat, openers);
        Assert.Contains(TrainingMovementPattern.Hinge, openers);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void StrengthKeepsItsCompoundLiftsFromBlockToBlock(int days)
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Strength, TrainingExperience.Intermediate, days, FullGym);
        var first = Block(profile, seed: 11);
        var deload = Block(profile, seed: 11, sequence: 2, previous: Used(first).ToList(), deload: true);
        var third = Block(profile, seed: 11, sequence: 3, previous: Used(deload).ToList());

        static List<List<string>> Compounds(MesocycleGeneration block) => Week(block, 1)
            .Select(session => session.Where(exercise => Pattern(exercise) is not (TrainingMovementPattern.Accessory or TrainingMovementPattern.Core))
                .Select(exercise => exercise.ExerciseId).ToList())
            .ToList();

        Assert.True(first.Succeeded && deload.Succeeded && third.Succeeded);
        Assert.Equal(Compounds(first), Compounds(deload));
        Assert.Equal(Compounds(first), Compounds(third));
    }

    [Fact]
    public void AStrengthPlanWithoutAnyHeavyLiftKeepsModerateRepetitions()
    {
        // Dumbbells only: no lift can be a low-repetition main lift, so nothing is prescribed at 3-6.
        var block = Block(PlanTestCatalog.Profile(TrainingGoal.Strength, TrainingExperience.Intermediate, 3, HomeDumbbells));

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        Assert.All(Week(block).SelectMany(session => session), exercise => Assert.True(exercise.MinRepetitions >= 5));
    }

    // ---- 3. Selection ------------------------------------------------------------------------------------------

    [Fact]
    public void TheBarbellBasicsCompeteInAFullGym()
    {
        var chosen = new HashSet<string>();
        foreach (var seed in Enumerable.Range(1, 12))
            chosen.UnionWith(Used(Block(PlanTestCatalog.Profile(days: 4, equipment: FullGym), seed)));

        Assert.Contains("squat", chosen);
        Assert.Contains("bench-press", chosen);
        Assert.Contains("romanian-deadlift", chosen);
        // The machines that used to crowd the barbell squat out are still chosen too.
        Assert.Contains(chosen, id => id is "leg-press" or "hack-squat");
    }

    [Fact]
    public void EquallyRatedExercisesAreNotResolvedAlphabetically()
    {
        // hack-squat and leg-press share priority, level and equipment: the plan seed decides, not the id.
        var firstMachineSquat = Enumerable.Range(1, 20)
            .Select(seed => Block(PlanTestCatalog.Profile(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 2, ["machine", "cable"]), seed))
            .Select(block => Week(block).SelectMany(session => session).First(exercise => Pattern(exercise) == TrainingMovementPattern.Squat).ExerciseId)
            .ToHashSet();

        Assert.Contains("hack-squat", firstMachineSquat);
        Assert.Contains("leg-press", firstMachineSquat);
    }

    [Theory]
    [InlineData(TrainingGoal.Hypertrophy)]
    [InlineData(TrainingGoal.Strength)]
    public void TheRackPullIsNotTheDefaultHinge(TrainingGoal goal)
    {
        foreach (var seed in Enumerable.Range(1, 8))
        {
            var block = Block(PlanTestCatalog.Profile(goal, TrainingExperience.Advanced, 4, FullGym), seed);
            Assert.True(block.Succeeded, string.Join("; ", block.Errors));
            // It has the highest curated priority of its pattern, but it does not train the hamstrings.
            var hinges = Week(block).SelectMany(session => session).Where(exercise => Pattern(exercise) == TrainingMovementPattern.Hinge).ToList();
            Assert.NotEmpty(hinges);
            Assert.Contains(hinges, exercise => exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Hamstrings));
            Assert.DoesNotContain(hinges.Take(1), exercise => exercise.ExerciseId == "rack-pull");
        }
    }

    [Theory]
    [InlineData(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingGoal.Hypertrophy, TrainingExperience.Advanced, 3)]
    [InlineData(TrainingGoal.Strength, TrainingExperience.Intermediate, 3)]
    [InlineData(TrainingGoal.GeneralFitness, TrainingExperience.Intermediate, 3)]
    public void ATrainedUserWithDumbbellsIsNotRegressedToBodyweightDrills(TrainingGoal goal, TrainingExperience experience, int days)
    {
        foreach (var seed in new[] { 1, 7, 42 })
        {
            var block = Block(PlanTestCatalog.Profile(goal, experience, days, HomeDumbbells), seed);
            Assert.True(block.Succeeded, string.Join("; ", block.Errors));
            // Repeating the dumbbell lift on two days is better than a push-up, an air squat or a glute bridge.
            Assert.Empty(Used(block).Intersect(["push-up", "knee-push-up", "bodyweight-squat", "glute-bridge"]));
        }
    }

    [Fact]
    public void ABeginnerInAGymDoesNotGetBodyweightRegressionsAsCompoundLifts()
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var days in new[] { 2, 3, 4 })
        {
            var block = Block(PlanTestCatalog.Profile(goal, TrainingExperience.Beginner, days, FullGym));
            Assert.True(block.Succeeded, string.Join("; ", block.Errors));
            Assert.Empty(Used(block).Intersect(["knee-push-up", "bodyweight-squat"]));
        }
    }

    [Fact]
    public void BodyweightExercisesAreStillUsedWhenNothingElseIsAvailable()
    {
        var block = Block(PlanTestCatalog.Profile(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 3, ["pull-up-bar", "resistance-band"]));

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        Assert.Contains("bodyweight-squat", Used(block));
    }

    // ---- 4. Volume balance -------------------------------------------------------------------------------------

    public static IEnumerable<object[]> GymProfiles()
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
            yield return [goal, experience, days];
    }

    [Theory]
    [MemberData(nameof(GymProfiles))]
    public void EveryGymPlanTrainsTheHamstringsDirectlyAndNeverLessThanTheCalves(TrainingGoal goal, TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: 60));
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        var week = Week(block).SelectMany(session => session).ToList();
        var hamstrings = DirectSets(week, TrainingMuscleGroup.Hamstrings);
        var calves = DirectSets(week, TrainingMuscleGroup.Calves);
        Assert.True(hamstrings >= 4, $"hamstrings get {hamstrings} direct sets");
        Assert.True(calves <= hamstrings, $"calves {calves} > hamstrings {hamstrings}");
        Assert.True(calves <= DirectSets(week, TrainingMuscleGroup.Chest), "calves above chest");
        Assert.True(calves <= DirectSets(week, TrainingMuscleGroup.Lats), "calves above lats");
        if (goal == TrainingGoal.Hypertrophy)
            Assert.True(hamstrings >= 6, $"hypertrophy hamstrings get {hamstrings} direct sets");
    }

    [Fact]
    public void CalvesStayOutOfStrengthPlansAndShortBeginnerPlans()
    {
        static int Calves(MesocycleGeneration block) => DirectSets(
            block.Sessions.SelectMany(session => PrescriptionJson.Read(session.PrescriptionJson)), TrainingMuscleGroup.Calves);

        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
            Assert.Equal(0, Calves(Block(PlanTestCatalog.Profile(TrainingGoal.Strength, experience, days, FullGym))));

        Assert.Equal(0, Calves(Block(PlanTestCatalog.Profile(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 2, FullGym, minutes: 45))));
        Assert.Equal(0, Calves(Block(PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Beginner, 3, FullGym, minutes: 40))));
        // They are still trained when there is room for them.
        Assert.True(Calves(Block(PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4, FullGym))) > 0);
    }

    [Theory]
    [MemberData(nameof(GymProfiles))]
    public void SingleLegWorkAndAccessoriesNeverGetALoneSet(TrainingGoal goal, TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: 60));
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        Assert.All(Week(block).SelectMany(session => session), exercise => Assert.True(exercise.Sets >= 2, $"{exercise.ExerciseId} has {exercise.Sets} set"));
    }

    [Theory]
    [InlineData(TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingExperience.Advanced, 4)]
    [InlineData(TrainingExperience.Intermediate, 2)]
    public void TheSquatIsNotStuckAtTwoSetsWhileAccessoriesGrow(TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, experience, days, FullGym));
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        var week = Week(block).SelectMany(session => session).ToList();
        var squats = week.Where(exercise => Pattern(exercise) == TrainingMovementPattern.Squat).ToList();
        Assert.Contains(squats, exercise => exercise.Sets >= 3);
        // Compounds take the weekly volume before the accessories do.
        var compoundSets = week.Where(exercise => Pattern(exercise) != TrainingMovementPattern.Accessory && Pattern(exercise) != TrainingMovementPattern.Core).Average(exercise => exercise.Sets);
        var accessorySets = week.Where(exercise => Pattern(exercise) == TrainingMovementPattern.Accessory).Average(exercise => exercise.Sets);
        Assert.True(compoundSets >= accessorySets, $"compounds {compoundSets:0.0} sets, accessories {accessorySets:0.0}");
    }

    [Theory]
    [MemberData(nameof(GymProfiles))]
    public void NoMuscleGoesBeyondTwentyWeeklySets(TrainingGoal goal, TrainingExperience experience, int days)
    {
        foreach (var minutes in new[] { 45, 90 })
        {
            var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: minutes));
            Assert.True(block.Succeeded, string.Join("; ", block.Errors));
            Assert.All(block.Quality!.WeeklyMuscleVolumes.SelectMany(week => week.Muscles),
                muscle => Assert.InRange(muscle.EstimatedEffectiveSets, 0m, 21m)); // 20 plus the peak-week set
        }
    }

    // ---- 5. Core -----------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(GymProfiles))]
    public void GeneralFitnessAndHypertrophyGetOneCoreExerciseAWeekAndStrengthNone(TrainingGoal goal, TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: 60));
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        foreach (var week in block.Sessions.GroupBy(session => session.WeekNumber))
        {
            var core = week.SelectMany(session => PrescriptionJson.Read(session.PrescriptionJson))
                .Where(exercise => Pattern(exercise) == TrainingMovementPattern.Core).ToList();
            Assert.Equal(goal == TrainingGoal.Strength ? 0 : 1, core.Count);
            Assert.All(core, exercise => Assert.Contains(TrainingMuscleGroup.Abdominals, exercise.PrimaryMuscleGroups));
        }
    }

    // ---- 6. Time -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 3, 60, 0.5)]
    [InlineData(TrainingGoal.GeneralFitness, TrainingExperience.Intermediate, 3, 60, 0.5)]
    [InlineData(TrainingGoal.Strength, TrainingExperience.Intermediate, 3, 75, 0.5)]
    // Four strength days of 90 minutes: three sets per secondary lift and the weekly ceilings stop a little earlier.
    [InlineData(TrainingGoal.Strength, TrainingExperience.Advanced, 4, 90, 0.4)]
    [InlineData(TrainingGoal.Hypertrophy, TrainingExperience.Beginner, 3, 60, 0.5)]
    [InlineData(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4, 75, 0.5)]
    public void SessionsUseAReasonableShareOfTheTimeOffered(TrainingGoal goal, TrainingExperience experience, int days, int minutes, double share)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: minutes));
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        var week = block.Sessions.Where(session => session.WeekNumber == 3).ToList();
        Assert.All(week, session => Assert.True(session.EstimatedDurationMinutes <= minutes, $"{session.Title}: {session.EstimatedDurationMinutes} min"));
        // On average about half of the time offered (it used to be a third for these profiles), never past it.
        var average = week.Average(session => session.EstimatedDurationMinutes);
        Assert.True(average >= minutes * share, $"sessions average {average:0} of {minutes} min");
    }

    [Fact]
    public void MoreTimeMeansMoreSetsOnTheCompoundLiftsNotMoreAccessorySets()
    {
        static (int Compound, int Accessory) Sets(MesocycleGeneration block)
        {
            var week = Week(block).SelectMany(session => session).ToList();
            return (week.Where(e => Pattern(e) is not (TrainingMovementPattern.Accessory or TrainingMovementPattern.Core)).Sum(e => e.Sets),
                week.Where(e => Pattern(e) == TrainingMovementPattern.Accessory).Sum(e => e.Sets));
        }

        var shortPlan = Sets(Block(PlanTestCatalog.Profile(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 3, FullGym, minutes: 45)));
        var longPlan = Sets(Block(PlanTestCatalog.Profile(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, 3, FullGym, minutes: 75)));

        Assert.True(longPlan.Compound > shortPlan.Compound, $"compound sets: {shortPlan.Compound} -> {longPlan.Compound}");
        Assert.True(longPlan.Compound - shortPlan.Compound >= longPlan.Accessory - shortPlan.Accessory);
    }

    [Theory]
    [MemberData(nameof(GymProfiles))]
    public void TheShortestSessionStillProducesAPlan(TrainingGoal goal, TrainingExperience experience, int days)
    {
        var block = Block(PlanTestCatalog.Profile(goal, experience, days, FullGym, minutes: 35));

        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        Assert.All(block.Sessions, session => Assert.InRange(session.EstimatedDurationMinutes, 1, 35));
    }
}
