using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Controlled scenarios over the real exercise library (the seed): not only that a plan is valid, but that its decisions are
/// reasonable. The bounds are product expectations, not physiological thresholds. Where the engine falls short today, the test
/// pins the exact shortfall so that fixing or widening it is a visible change. See docs/validation-scenarios.md.
/// </summary>
public sealed class DecisionScenarioTests
{
    private static readonly (TrainingProgramSplit Split, int Days)[] Layouts =
    [
        (TrainingProgramSplit.Automatic, 2), (TrainingProgramSplit.Automatic, 3), (TrainingProgramSplit.Automatic, 4),
        (TrainingProgramSplit.PushPullLegs, 5), (TrainingProgramSplit.PushPullLegs, 6)
    ];

    private static readonly (string Name, string[] Items)[] Equipment =
    [
        ("full gym", []),
        ("dumbbells and bench", ["dumbbell", "bench"]),
        ("barbell, rack and pull-up bar", ["barbell", "bench", "squat-rack", "pull-up-bar"]),
        ("band only", ["resistance-band"]),
        ("bodyweight only", ["step"])
    ];

    private static readonly TrainingMuscleGroup[] MajorMuscles =
    [
        TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Quadriceps,
        TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Deltoids
    ];

    private sealed record Scenario(
        TrainingGoal Goal, TrainingExperience Level, TrainingProgramSplit Split, int Days, string Equipment, int Minutes,
        MesocycleGeneration Result)
    {
        public string Name => $"{Goal} {Level} {Split} {Days}d, {Equipment}, {Minutes}'";

        /// <summary>
        /// The prescriptions of the third week of the block, one list per session. The first two weeks of a hypertrophy block are
        /// introductory (one set less in many exercises), so the weekly dose is judged on a training week.
        /// </summary>
        public List<IReadOnlyList<TrainingExercisePrescription>> Week => TrainingWeek(Result);

        /// <summary>Sessions of the week that give the muscle at least two effective sets.</summary>
        public int Exposures(TrainingMuscleGroup muscle) =>
            Week.Count(session => session.Where(e => e.StimulatedMuscles().Contains(muscle)).Sum(e => e.Sets) >= 2);
    }

    private static readonly Lazy<IReadOnlyList<Scenario>> All = new(() => (
        from goal in Enum.GetValues<TrainingGoal>()
        from level in Enum.GetValues<TrainingExperience>()
        from layout in Layouts
        from equipment in Equipment
        from minutes in new[] { 45, 75 }
        select new Scenario(goal, level, layout.Split, layout.Days, equipment.Name, minutes, Generate(
            PlanTestCatalog.Profile(goal, level, layout.Days, equipment.Items, null, null, 12, minutes, layout.Split)))).ToList());

    private static List<IReadOnlyList<TrainingExercisePrescription>> TrainingWeek(MesocycleGeneration result) =>
        result.Sessions.Where(s => s.WeekNumber == 3).Select(s => PrescriptionJson.Read(s.PrescriptionJson)).ToList();

    private static MesocycleGeneration Generate(PlanProfile profile) =>
        MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());

    private static IEnumerable<Scenario> Planned => All.Value.Where(s => s.Result.Succeeded);

    private static IEnumerable<Scenario> FullGymHypertrophy =>
        Planned.Where(s => s is { Goal: TrainingGoal.Hypertrophy, Equipment: "full gym", Minutes: 75 });

    private static void None(IEnumerable<string> offences) => Assert.Equal([], offences.ToArray());

    [Fact]
    public void AGymOrDumbbellsAlwaysGiveAPlan() =>
        None(All.Value.Where(s => s.Equipment is "full gym" or "dumbbells and bench" && !s.Result.Succeeded)
            .Select(s => $"{s.Name}: {string.Join("; ", s.Result.Errors)}"));

    [Fact]
    public void ThePlansThatCannotBeBuiltAreTheKnownOnes_AndSayWhichPatternIsMissing()
    {
        // Equipment, goal and the movement pattern with nothing to offer. A change here is a change of what users can get.
        string[] known =
        [
            "bodyweight only|GeneralFitness|HorizontalPull", "bodyweight only|Hypertrophy|HorizontalPull",
            "bodyweight only|Strength|HorizontalPull"
        ];

        var failed = All.Value.Where(s => !s.Result.Succeeded)
            .Select(s => $"{s.Equipment}|{s.Goal}|{s.Result.Errors[0].Split('\'')[1]}").Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(known.Order(StringComparer.Ordinal).ToArray(), failed);
    }

    [Fact]
    public void NoSessionIsLongerThanTheTimeOffered() =>
        None(Planned.Where(s => s.Result.Sessions.Any(x => x.EstimatedDurationMinutes > s.Minutes)).Select(s => s.Name));

    [Fact]
    public void NoMuscleGoesOverTwentyWeeklySets_ExceptTheGlutesOfStrengthWithNoHeavyLegLift()
    {
        // Known gap (priority P7): in the peak weeks of an advanced strength plan with only dumbbells, or only bands (a plan
        // that could not be built before engine 0.18.0), the glutes go just past twenty.
        var offences = Planned.SelectMany(s => PriorityDelivery.WeeklySets(s.Week).Where(m => m.Value > 20m)
            .Select(m => $"{s.Name}: {m.Key} {m.Value:0}")).ToArray();

        Assert.True(offences.SequenceEqual(
        [
            "Strength Advanced Automatic 3d, dumbbells and bench, 45': Glutes 21",
            "Strength Advanced Automatic 3d, dumbbells and bench, 75': Glutes 21",
            "Strength Advanced Automatic 3d, band only, 45': Glutes 21",
            "Strength Advanced Automatic 3d, band only, 75': Glutes 23"
        ]), string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void InAGym_EveryMajorMuscleGetsAUsefulWeeklyDose() =>
        None(FullGymHypertrophy.SelectMany(s => MajorMuscles
            .Select(m => (Muscle: m, Sets: PriorityDelivery.WeeklySets(s.Week).GetValueOrDefault(m)))
            .Where(x => x.Sets < 6m).Select(x => $"{s.Name}: {x.Muscle} {x.Sets}")));

    [Fact]
    public void InAGymWithThreeDaysOrMore_MajorMusclesAreTrainedTwiceAWeek_ExceptTheLegsOfAFiveDayPushPullLegs()
    {
        var shortfalls = FullGymHypertrophy.Where(s => s.Days >= 3)
            .SelectMany(s => MajorMuscles.Where(m => s.Exposures(m) < 2).Select(m => (s.Days, s.Split, Muscle: m)))
            .Distinct().Select(x => $"{x.Split} {x.Days}d {x.Muscle}").Order(StringComparer.Ordinal).ToArray();

        // Known gap (priority P7): the calendar five day push/pull/legs has a single leg day.
        Assert.Equal(
            ["PushPullLegs 5d Glutes", "PushPullLegs 5d Hamstrings", "PushPullLegs 5d Quadriceps"], shortfalls);
    }

    [Fact]
    public void ATrainedUserIsNotGivenClearlyLessWorkThanABeginner()
    {
        var sets = Planned.ToDictionary(s => (s.Goal, s.Split, s.Days, s.Equipment, s.Minutes, s.Level), s => s.Week.Sum(x => x.Sum(e => e.Sets)));

        // Two sets of slack: with little time the heavier prescriptions of a trained user leave room for slightly fewer sets.
        None(sets.Where(p => p.Key.Level == TrainingExperience.Advanced &&
                sets.TryGetValue(p.Key with { Level = TrainingExperience.Beginner }, out var beginner) && p.Value < beginner - 2)
            .Select(p => $"{p.Key}: {p.Value}"));
    }

    [Theory]
    [InlineData(TrainingMuscleGroup.Chest)]
    [InlineData(TrainingMuscleGroup.Lats)]
    [InlineData(TrainingMuscleGroup.Quadriceps)]
    [InlineData(TrainingMuscleGroup.Deltoids)]
    public void APriorityChangesTheDoseInItsDirection(TrainingMuscleGroup muscle)
    {
        // An intermediate with four days of 75 minutes in a full gym.
        decimal Dose(TrainingMusclePriority priority) => PriorityDelivery.WeeklySets(TrainingWeek(Generate(Prioritized(
            TrainingExperience.Intermediate, 4, 75, muscle, priority)))).GetValueOrDefault(muscle);

        var normal = Dose(TrainingMusclePriority.Grow);

        Assert.True(Dose(TrainingMusclePriority.Emphasize) > normal, "Emphasize adds sets");
        Assert.True(Dose(TrainingMusclePriority.Maintain) < normal, "Maintain lowers the dose");
    }

    private static PlanProfile Prioritized(
        TrainingExperience level, int days, int minutes, TrainingMuscleGroup muscle, TrainingMusclePriority priority) =>
        PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, level, days, minutes: minutes) with
        {
            MusclePriorities = new Dictionary<TrainingMuscleGroup, TrainingMusclePriority> { [muscle] = priority }
        };

    [Fact]
    public void AnEmphasizedMuscleReachesItsTargetWhenThereAreThreeDaysOrMoreAndLongSessions_AndNeverPassesTwentySets()
    {
        var short_ = new List<string>();
        var over = new List<string>();
        foreach (var level in Enum.GetValues<TrainingExperience>())
        foreach (var days in new[] { 2, 3, 4 })
        foreach (var minutes in new[] { 60, 75, 90 })
        foreach (var muscle in MajorMuscles.Append(TrainingMuscleGroup.Biceps).Append(TrainingMuscleGroup.Triceps))
        {
            var profile = Prioritized(level, days, minutes, muscle, TrainingMusclePriority.Emphasize);
            var sets = PriorityDelivery.WeeklySets(TrainingWeek(Generate(profile)));
            var target = MesocycleGenerator.Baselines(profile).Single(b => b.Muscle == muscle).Target;
            over.AddRange(sets.Where(m => m.Value > 20m).Select(m => $"{level} {days}d {minutes}' {muscle}: {m.Key} {m.Value}"));
            if (days >= 3 && minutes >= 75 && sets.GetValueOrDefault(muscle) < target - 1)
                short_.Add($"{level} {days}d {minutes}' {muscle}: {sets.GetValueOrDefault(muscle)}/{target}");
        }

        None(over);
        // Known gap (priority P7): the upper back of an advanced user, which has little direct work to offer.
        Assert.All(short_, line => Assert.Contains("UpperBack", line));
    }

    [Theory]
    [InlineData("Knee")]
    [InlineData("Shoulder")]
    [InlineData("LowerBack")]
    [InlineData("Wrist")]
    [InlineData("Elbow")]
    [InlineData("Knee", "Shoulder", "LowerBack", "Wrist", "Elbow")]
    public void ALimitationStillGivesAGymPlan_WithoutAnythingItRulesOut(params string[] areas)
    {
        var catalog = RealCatalog.Candidates();
        var ruledOut = LimitationRules.ExcludedExerciseIds(areas, catalog).ToHashSet();
        foreach (var level in Enum.GetValues<TrainingExperience>())
        {
            var result = Generate(PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, level, 4, limitations: areas));

            Assert.True(result.Succeeded, $"{level}: {string.Join("; ", result.Errors)}");
            Assert.DoesNotContain(result.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)),
                e => ruledOut.Contains(e.ExerciseId));
        }
    }
}
