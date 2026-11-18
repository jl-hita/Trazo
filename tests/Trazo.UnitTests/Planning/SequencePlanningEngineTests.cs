using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The sequence (asynchronous) mode of the single generation engine: the sessions follow ONE continuous rotation whatever the period or
/// block boundary, every supported split works with any number of sessions per period, and the plans keep the quality of the classical
/// ones (limits, equipment, level, exclusions) while their volume is judged per seven days, not per group of sessions.
/// </summary>
public sealed class SequencePlanningEngineTests
{
    private static readonly string[] FullGym = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    private static readonly string[] FullBodyCycle = ["Cuerpo completo A", "Cuerpo completo B"];
    private static readonly string[] UpperLowerCycle = ["Tren superior A", "Tren inferior A", "Tren superior B", "Tren inferior B"];
    private static readonly string[] PushPullLegsCycle = ["Empuje", "Tracción", "Pierna", "Empuje B", "Tracción B", "Pierna B"];

    /// <summary>What a trainer expects the rotation of a split to be (written independently of the engine's tables).</summary>
    private static string[] CycleOf(TrainingProgramSplit split, int sessions) => (split == TrainingProgramSplit.Automatic
        ? sessions switch { <= 3 => TrainingProgramSplit.FullBody, <= 5 => TrainingProgramSplit.UpperLower, _ => TrainingProgramSplit.PushPullLegs }
        : split) switch
    {
        TrainingProgramSplit.FullBody => FullBodyCycle,
        TrainingProgramSplit.UpperLower => UpperLowerCycle,
        _ => PushPullLegsCycle
    };

    private static PlanProfile Sequence(
        TrainingGoal goal, TrainingExperience experience, int sessions, TrainingProgramSplit split, string[]? equipment = null,
        string[]? limitations = null, int minutes = 75) =>
        PlanTestCatalog.Profile(goal, experience, sessions, equipment ?? FullGym, null, limitations, 12, minutes, split) with
        {
            SchedulingMode = PlanSchedulingMode.Sequence, SessionsPerPeriod = sessions, TrainingDays = []
        };

    private static MesocycleGeneration Generate(PlanProfile profile, int periods = 4, int position = 0, bool deload = false, int seed = 7) =>
        MesocycleGenerator.Generate(profile, seed, new MesocycleSlot(1, 1, deload ? "deload" : "accumulation", 1, deload ? 1 : periods),
            PlanTestCatalog.Candidates(), rotationPosition: position);

    public static IEnumerable<object[]> SupportedCombinations()
    {
        foreach (var split in Enum.GetValues<TrainingProgramSplit>())
        foreach (var sessions in Enumerable.Range(2, 5))
            if (TrainingSessionRotation.IsSupportedSequence(split, sessions)) yield return [split, sessions];
    }

    [Fact]
    public void TheSupportedCombinationsAreExactlyTheOnesTheProductAsked()
    {
        // Full body 2-6, upper/lower 2-6, push/pull/legs 3-6 and automatic 2-6.
        Assert.Equal(5 + 5 + 4 + 5, SupportedCombinations().Count());
        Assert.False(TrainingSessionRotation.IsSupportedSequence(TrainingProgramSplit.PushPullLegs, 2));
        Assert.False(TrainingSessionRotation.IsSupportedSequence(TrainingProgramSplit.FullBody, 1));
        Assert.False(TrainingSessionRotation.IsSupportedSequence(TrainingProgramSplit.FullBody, 7));
    }

    [Fact]
    public void TheClassicalCombinationsAreNotRelaxedForTheSakeOfTheSequence()
    {
        Assert.False(TrainingSessionRotation.IsSupportedWeekly(TrainingProgramSplit.UpperLower, 3));
        Assert.False(TrainingSessionRotation.IsSupportedWeekly(TrainingProgramSplit.UpperLower, 5));
        Assert.False(TrainingSessionRotation.IsSupportedWeekly(TrainingProgramSplit.PushPullLegs, 4));
        Assert.False(TrainingSessionRotation.IsSupportedWeekly(TrainingProgramSplit.FullBody, 5));
        Assert.False(TrainingSessionRotation.IsSupportedWeekly(TrainingProgramSplit.Automatic, 5));

        // The calendar plan still fails with the engine's own explanation.
        var calendar = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 3, FullGym, split: TrainingProgramSplit.UpperLower);
        var result = MesocycleGenerator.Generate(calendar, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), PlanTestCatalog.Candidates());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Torso/Pierna admite 2 o 4 días"));
    }

    [Fact]
    public void AnUnsupportedSequenceIsRejectedWithAnExplanation_NeverHalfPlanned()
    {
        var result = Generate(Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 2, TrainingProgramSplit.PushPullLegs));
        Assert.False(result.Succeeded);
        Assert.Empty(result.Sessions);
        Assert.Contains(result.Errors, e => e.Contains("Push/Pull/Legs admite de 3 a 6"));
    }

    [Theory]
    [MemberData(nameof(SupportedCombinations))]
    public void EverySupportedCombinationGeneratesWholePeriodsWithNoDates(TrainingProgramSplit split, int sessions)
    {
        var result = Generate(Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, sessions, split));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(4 * sessions, result.Sessions.Count);
        Assert.All(result.Sessions, s => { Assert.Null(s.Date); Assert.Null(s.Day); });
        Assert.Equal(Enumerable.Range(1, 4), result.Sessions.Select(s => s.WeekNumber).Distinct());
        Assert.All(result.Sessions.GroupBy(s => s.WeekNumber), period => Assert.Equal(sessions, period.Count()));
    }

    [Theory]
    [MemberData(nameof(SupportedCombinations))]
    public void TheRotationNeverRestartsAtThePeriodBoundary(TrainingProgramSplit split, int sessions)
    {
        var cycle = CycleOf(split, sessions);
        var result = Generate(Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, sessions, split));
        Assert.Equal(
            Enumerable.Range(0, 4 * sessions).Select(position => cycle[position % cycle.Length]),
            result.Sessions.Select(s => s.Title));
    }

    [Fact]
    public void AnOddSplitCrossesThePeriodBoundary_UpperLowerInThreeSessionsAlternatesWhichSideHasTwo()
    {
        var result = Generate(Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 3, TrainingProgramSplit.UpperLower));
        var periods = result.Sessions.GroupBy(s => s.WeekNumber).Select(p => p.Select(s => s.Title[5..].Trim()).ToArray()).ToArray();
        Assert.Equal(["superior A", "inferior A", "superior B"], periods[0]);
        Assert.Equal(["inferior B", "superior A", "inferior A"], periods[1]); // period 2 does not begin with "superior A" again
        Assert.Equal(["superior B", "inferior B", "superior A"], periods[2]);
    }

    [Theory]
    [InlineData(TrainingProgramSplit.PushPullLegs, 4)]
    [InlineData(TrainingProgramSplit.PushPullLegs, 5)]
    [InlineData(TrainingProgramSplit.UpperLower, 3)]
    [InlineData(TrainingProgramSplit.FullBody, 3)]
    [InlineData(TrainingProgramSplit.Automatic, 5)]
    public void ABlockBeginsWhereThePreviousOneEnded_ThePositionIsAllItNeeds(TrainingProgramSplit split, int sessions)
    {
        var profile = Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, sessions, split);
        var first = Generate(profile, periods: 4);
        var second = Generate(profile, periods: 4, position: first.Sessions.Count);
        var cycle = CycleOf(split, sessions);
        var titles = first.Sessions.Concat(second.Sessions).Select(s => s.Title).ToArray();
        Assert.Equal(Enumerable.Range(0, 8 * sessions).Select(p => cycle[p % cycle.Length]), titles);
    }

    [Fact]
    public void ABlockStartedInTheMiddleOfTheRotationStartsWithTheNextSession()
    {
        // Four sessions per period of push/pull/legs: the first block ends on session 16 ("Empuje B"), the next one starts with "Tracción B".
        var profile = Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4, TrainingProgramSplit.PushPullLegs);
        var first = Generate(profile, periods: 4);
        Assert.Equal("Empuje B", first.Sessions[^1].Title);
        var next = Generate(profile, periods: 4, position: first.Sessions.Count);
        Assert.Equal("Tracción B", next.Sessions[0].Title);
    }

    [Fact]
    public void TheDeloadBlockContinuesTheRotation_AndIsLighterThanATrainingPeriod()
    {
        var profile = Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 4, TrainingProgramSplit.PushPullLegs);
        var training = Generate(profile, periods: 4);
        var deload = Generate(profile, position: training.Sessions.Count, deload: true);
        Assert.True(deload.Succeeded, string.Join("; ", deload.Errors));
        Assert.Equal(["Tracción B", "Pierna B", "Empuje", "Tracción"], deload.Sessions.Select(s => s.Title));
        int Sets(IEnumerable<GeneratedSession> s) => s.Sum(x => PrescriptionJson.Read(x.PrescriptionJson).Sum(e => e.Sets));
        Assert.True(Sets(deload.Sessions) < Sets(training.Sessions.Take(4)), "a deload period must be lighter than a training period");
    }

    // ------------------------------------------------------------------ one engine: the same plan as the calendar when the periods allow it

    [Theory]
    [InlineData(TrainingProgramSplit.FullBody, 2)]
    [InlineData(TrainingProgramSplit.UpperLower, 4)]
    [InlineData(TrainingProgramSplit.PushPullLegs, 6)]
    [InlineData(TrainingProgramSplit.Automatic, 2)]
    [InlineData(TrainingProgramSplit.Automatic, 4)]
    public void WhenAPeriodHoldsAWholeSplit_TheSequencePlanIsTheCalendarPlan(TrainingProgramSplit split, int sessions)
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in new[] { TrainingExperience.Beginner, TrainingExperience.Advanced })
        {
            var sequence = Generate(Sequence(goal, experience, sessions, split));
            var calendar = MesocycleGenerator.Generate(
                // Not on a Sunday: the allocator breaks ties by the order of the sessions and a Sunday sorts first (enum order), a
                // habit of the calendar engine that the golden test keeps as it is.
                PlanTestCatalog.Profile(goal, experience, sessions, FullGym, split: split) with
                {
                    TrainingDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }.Take(sessions).ToList()
                }, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4),
                PlanTestCatalog.Candidates());
            Assert.True(sequence.Succeeded && calendar.Succeeded);
            Assert.Equal(
                calendar.Sessions.Select(s => (s.WeekNumber, s.Title, s.EstimatedDurationMinutes, s.PrescriptionJson)),
                sequence.Sessions.Select(s => (s.WeekNumber, s.Title, s.EstimatedDurationMinutes, s.PrescriptionJson)));
        }
    }

    // ------------------------------------------------------------------ quality

    public static IEnumerable<object[]> QualityGrid()
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var combination in SupportedCombinations().Where(c => (int)c[1] is 3 or 5 or 6 || (TrainingProgramSplit)c[0] != TrainingProgramSplit.Automatic))
        foreach (var equipment in new[] { 0, 1 })
            yield return [goal, experience, combination[0], combination[1], equipment];
    }

    private static readonly string[][] EquipmentSets = [FullGym, ["dumbbell", "bench"]];

    [Theory]
    [MemberData(nameof(QualityGrid))]
    public void EveryPlanRespectsTheClassicalLimits_AndItsDoseIsJudgedPerSevenDays(
        TrainingGoal goal, TrainingExperience experience, TrainingProgramSplit split, int sessions, int equipmentIndex)
    {
        var equipment = EquipmentSets[equipmentIndex];
        var result = Generate(Sequence(goal, experience, sessions, split, equipment));
        if (!result.Succeeded)
        {
            Assert.NotEmpty(result.Errors); // an impossible profile is explained, never half planned
            Assert.Empty(result.Sessions);
            return;
        }

        var catalog = PlanTestCatalog.Candidates().ToDictionary(c => c.ExerciseId);
        foreach (var session in result.Sessions)
        {
            var exercises = PrescriptionJson.Read(session.PrescriptionJson);
            Assert.InRange(session.EstimatedDurationMinutes, 1, 75);
            Assert.Equal(exercises.Count, exercises.Select(e => e.ExerciseId).Distinct().Count());
            foreach (var exercise in exercises)
            {
                var candidate = catalog[exercise.ExerciseId];
                Assert.True((int)candidate.MinimumExperience <= (int)experience, $"{exercise.ExerciseId} is above the level");
                Assert.Contains(goal, candidate.SuitableGoals);
                Assert.All(candidate.RequiredEquipment, item =>
                    Assert.True(equipment.Contains(item) || (item.EndsWith("-machine") && equipment.Contains("machine")), $"{exercise.ExerciseId} needs {item}"));
                Assert.InRange(exercise.Sets, 1, 5);
            }
        }

        // The dose per seven days (sets of one pass through the rotation divided by the nominal weeks it spans) is judged against the
        // engine's review ceiling of 20 effective sets exactly as a weekly dose is in a calendar plan: the allocator never adds a set
        // past it, and when the minimum dose of the exercises alone goes over it (full body six times a week) the plan says so in a
        // warning instead of hiding it. The main muscles are really trained.
        var report = result.Quality!;
        Assert.True(report.WeeksPerGroup > 0m);
        var perWeek = report.WeeklyMuscleVolumes.Single().Muscles;
        foreach (var m in perWeek.Where(m => m.EstimatedEffectiveSets / report.WeeksPerGroup > 20m))
            Assert.Contains(report.Warnings, w => w.Contains(m.MuscleGroup.ToString()) && w.Contains("acumula"));
        foreach (var muscle in new[] { TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Quadriceps })
        {
            var dose = perWeek.Single(m => m.MuscleGroup == muscle).EstimatedEffectiveSets / report.WeeksPerGroup;
            if (equipmentIndex == 0) // a full gym can always train them
                Assert.True(dose >= (goal == TrainingGoal.GeneralFitness ? 4m : 6m), $"{muscle}: only {dose:0.#} effective sets a week");
        }
    }

    [Fact]
    public void LimitationsAndExclusionsAreHonouredInASequencePlanToo()
    {
        var profile = Sequence(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, 5, TrainingProgramSplit.UpperLower, limitations: ["Knee", "Shoulder"]);
        var result = Generate(profile);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var forbidden = LimitationRules.ExcludedExerciseIds(profile.Limitations, PlanTestCatalog.Candidates()).ToHashSet();
        Assert.NotEmpty(forbidden);
        Assert.DoesNotContain(result.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)), e => forbidden.Contains(e.ExerciseId));
    }

    [Fact]
    public void AGivenSeedAndPositionAlwaysGiveTheSamePlan()
    {
        var profile = Sequence(TrainingGoal.Strength, TrainingExperience.Advanced, 5, TrainingProgramSplit.Automatic);
        var a = Generate(profile, position: 7);
        var b = Generate(profile, position: 7);
        Assert.Equal(a.Sessions.Select(s => s.PrescriptionJson), b.Sessions.Select(s => s.PrescriptionJson));
    }
}
