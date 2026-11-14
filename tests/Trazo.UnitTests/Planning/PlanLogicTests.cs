using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class PlanProfileParserTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static PlanProfileInput Valid() => new(
        "Hypertrophy", "Intermediate", null, null, ["Monday", "Wednesday", "Friday"], null, ["Dumbbell", "Pull_Up bar"],
        ["Bench-Press"], ["lower back"], null, null, null);

    [Fact]
    public void ParsesAValidProfileAndAppliesDefaults()
    {
        var result = PlanProfileParser.Parse(Valid(), Today);

        Assert.True(result.Succeeded);
        var profile = result.Profile!;
        Assert.Equal(TrainingGoal.Hypertrophy, profile.Goal);
        Assert.Equal(Today, profile.StartDate);
        Assert.Equal(12, profile.TotalWeeks);
        Assert.Equal(60, profile.MaxSessionMinutes);
        Assert.Equal(["dumbbell", "pull-up-bar"], profile.AvailableEquipment); // normalised
        Assert.Equal(["bench-press"], profile.ExcludedExerciseIds);
        Assert.Equal(["LowerBack"], profile.Limitations);
        Assert.True(profile.IncludeDeloadPhases);
    }

    [Theory]
    [InlineData("goal", "Bulking")]
    [InlineData("goal", "1")]
    [InlineData("experience", "Elite")]
    [InlineData("split", "BroSplit")]
    public void RejectsUnknownEnumValues(string field, string value)
    {
        var input = Valid() with
        {
            Goal = field == "goal" ? value : "Hypertrophy",
            Experience = field == "experience" ? value : "Beginner",
            Split = field == "split" ? value : null
        };

        var result = PlanProfileParser.Parse(input, Today);

        Assert.False(result.Succeeded);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void RejectsEveryOutOfRangeField_AndReportsAllOfThem()
    {
        var input = new PlanProfileInput(
            "x", "y", Today.AddDays(200), 3, ["Monday", "Monday", "Funday"], 20, ["bad token!", new string('a', 50)],
            Enumerable.Range(0, 60).Select(i => $"ex-{i}").ToArray(), ["Neck"], "x", true, 2);

        var errors = PlanProfileParser.Parse(input, Today).Errors;

        foreach (var key in new[] { "goal", "experience", "startDate", "totalWeeks", "trainingDays", "maxSessionMinutes",
                     "availableEquipment", "excludedExerciseIds", "limitations", "split", "deloadEveryTrainingWeeks" })
            Assert.Contains(key, errors.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void RequiresBetweenTwoAndSixTrainingDays(int count)
    {
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }.Take(count).ToArray();

        var result = PlanProfileParser.Parse(Valid() with { TrainingDays = days }, Today);

        Assert.Contains("trainingDays", result.Errors.Keys);
    }

    [Fact]
    public void LimitsTheStartDateWindow()
    {
        Assert.True(PlanProfileParser.Parse(Valid() with { StartDate = Today.AddDays(-7) }, Today).Succeeded);
        Assert.False(PlanProfileParser.Parse(Valid() with { StartDate = Today.AddDays(-8) }, Today).Succeeded);
        Assert.True(PlanProfileParser.Parse(Valid() with { StartDate = Today.AddDays(60) }, Today).Succeeded);
        Assert.False(PlanProfileParser.Parse(Valid() with { StartDate = Today.AddDays(61) }, Today).Succeeded);
    }
}

public sealed class LimitationRulesTests
{
    [Theory]
    [InlineData("knee", "Knee")]
    [InlineData("Lower Back", "LowerBack")]
    [InlineData("lower_back", "LowerBack")]
    [InlineData("SHOULDER", "Shoulder")]
    public void NamesAreCanonical(string input, string expected) => Assert.Equal(expected, LimitationRules.Canonical(input));

    [Fact]
    public void UnknownAreasAreRejected() => Assert.Null(LimitationRules.Canonical("elbow-ish"));

    [Fact]
    public void ExcludesByIdTokensAndLeavesTheRestAlone()
    {
        var candidates = PlanTestCatalog.Candidates();

        var knee = LimitationRules.ExcludedExerciseIds(["Knee"], candidates);
        var none = LimitationRules.ExcludedExerciseIds([], candidates);

        Assert.Contains("lunge", knee);
        Assert.Contains("bulgarian-split-squat", knee);
        Assert.DoesNotContain("goblet-squat", knee);
        Assert.Empty(none);
    }

    [Fact]
    public void EveryAreaExcludesSomethingFromARealisticCatalogButNeverEverything()
    {
        var candidates = PlanTestCatalog.Candidates();
        foreach (var area in LimitationRules.Areas)
        {
            var excluded = LimitationRules.ExcludedExerciseIds([area], candidates);
            Assert.True(excluded.Count < candidates.Count / 2, $"{area} excludes too much");
        }
    }
}

public sealed class PlanScheduleTests
{
    private static readonly DateOnly Start = new(2026, 11, 18); // a Wednesday

    [Fact]
    public void ACycleIsCutIntoBlocksOfAtMostFourWeeks_CoveringEveryWeekOnce()
    {
        var draft = TrainingMacrocycleDraftBuilder.Build(new(Start, 12, TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, true, 4)).Draft!;

        var slots = PlanSchedule.Slots(draft);

        Assert.All(slots, slot => Assert.InRange(slot.Weeks, 1, PlanSchedule.MaxMesocycleWeeks));
        Assert.Equal(Enumerable.Range(1, 12), slots.SelectMany(s => Enumerable.Range(s.StartWeek, s.Weeks)));
        Assert.Equal(slots.Count, slots.Select(s => s.Sequence).Distinct().Count());
        Assert.All(slots.Where(s => s.IsDeload), slot => Assert.Equal(1, slot.Weeks));
        Assert.Contains(slots, s => s.IsDeload); // 12 weeks with a deload every 4 training weeks
    }

    [Fact]
    public void LongPhasesAreSplitAndKeepTheirPhaseNumber()
    {
        var draft = TrainingMacrocycleDraftBuilder.Build(new(Start, 10, TrainingGoal.GeneralFitness, TrainingExperience.Beginner, false, 4)).Draft!;

        var slots = PlanSchedule.Slots(draft);

        Assert.Equal([4, 4, 2], slots.Select(s => s.Weeks));
        Assert.All(slots, slot => Assert.Equal(1, slot.PhaseSequenceNumber));
    }

    [Theory]
    [InlineData(DayOfWeek.Wednesday, 1, "2026-11-18")]
    [InlineData(DayOfWeek.Friday, 1, "2026-11-20")]
    [InlineData(DayOfWeek.Monday, 1, "2026-11-23")] // before the start weekday: falls later in the same 7-day week
    [InlineData(DayOfWeek.Wednesday, 3, "2026-12-02")]
    public void DatesStayInsideTheirWeek(DayOfWeek day, int week, string expected)
    {
        var date = PlanSchedule.DateFor(Start, week, day);

        Assert.Equal(DateOnly.Parse(expected), date);
        Assert.InRange(date, PlanSchedule.WeekStart(Start, week), PlanSchedule.WeekStart(Start, week).AddDays(6));
    }

    [Theory]
    [InlineData("2026-11-10", PlanTimeline.NotStarted, 1)]
    [InlineData("2026-11-18", PlanTimeline.InProgress, 1)]
    [InlineData("2026-11-24", PlanTimeline.InProgress, 1)]
    [InlineData("2026-11-25", PlanTimeline.InProgress, 2)]
    [InlineData("2026-12-15", PlanTimeline.InProgress, 4)]
    [InlineData("2026-12-16", PlanTimeline.Finished, 4)]
    public void PositionFollowsTheCalendar(string today, PlanTimeline state, int week)
    {
        var position = PlanSchedule.Position(Start, 4, DateOnly.Parse(today));

        Assert.Equal(state == PlanTimeline.Finished ? new PlanPosition(state, 4) : new PlanPosition(state, week), position);
    }
}

public sealed class PrescriptionJsonTests
{
    [Fact]
    public void RoundTripsAGeneratedSession_AndTheAdjusterCanEditIt()
    {
        var block = MesocycleGenerator.Generate(PlanTestCatalog.Profile(), 1, new MesocycleSlot(1, 1, "accumulation", 1, 4), PlanTestCatalog.Candidates());
        var json = block.Sessions[0].PrescriptionJson;
        var exercise = PrescriptionJson.Read(json)[0];

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(json, exercise.ExerciseId, 62.5m, "test", out var withLoad, out _));
        Assert.Equal(62.5m, PrescriptionJson.Read(withLoad)[0].TargetLoadKg);

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(withLoad, out var deloaded, out _));
        Assert.True(PrescriptionJson.IsAdaptationDeload(deloaded));
        var after = PrescriptionJson.Read(deloaded);
        Assert.All(after, e => Assert.Equal(TrainingProgressionRule.None, e.ProgressionRule)); // the adjuster writes it as a number
        Assert.True(after[0].Sets <= exercise.Sets);
        Assert.Equal(exercise.Sets, PrescriptionJson.OriginalSets(deloaded)[exercise.ExerciseId]);
    }

    [Fact]
    public void RemembersWhereASwappedExerciseCameFrom()
    {
        var block = MesocycleGenerator.Generate(PlanTestCatalog.Profile(), 1, new MesocycleSlot(1, 1, "accumulation", 1, 4), PlanTestCatalog.Candidates());
        var json = block.Sessions[0].PrescriptionJson;
        var current = PrescriptionJson.Read(json)[0];
        var replacement = PlanTestCatalog.Candidates().First(c => c.MovementPattern == TrainingMovementPattern.Accessory
            && !PrescriptionJson.Read(json).Any(e => e.ExerciseId == c.ExerciseId));

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(json, current.ExerciseId, replacement, out var swapped, out _));

        Assert.Equal(current.ExerciseId, PrescriptionJson.SwappedFrom(swapped, replacement.ExerciseId));
        Assert.Null(PrescriptionJson.SwappedFrom(json, current.ExerciseId));
    }
}

public sealed class AdaptationPlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);

    private static SetRecord Set(decimal kg, int reps, decimal rir = 2, bool done = true, string? skipped = null, bool pain = false) =>
        new(kg, reps, rir, done, skipped, pain);

    private static ExerciseExposureRecord Exposure(int day, IReadOnlyList<SetRecord> sets, WorkoutOutcome outcome = WorkoutOutcome.Completed,
        string recovery = "Good", int planned = 3, bool eligible = true, string id = "bench-press", int? original = null) =>
        new(T0.AddDays(day), id, planned, 6, 10, 2m, TrainingProgressionRule.DoubleProgression, eligible, sets, outcome, recovery, original);

    private static SessionRecord Session(int day, WorkoutOutcome outcome = WorkoutOutcome.Completed, string recovery = "Good",
        string reason = "None", bool pain = false, decimal? rpe = 7, decimal? completion = 1m) =>
        new(T0.AddDays(day), outcome, reason, recovery, pain, rpe, completion);

    private static AdaptationOutcome Run(IReadOnlyList<ExerciseExposureRecord> exposures, IReadOnlyList<SessionRecord> sessions,
        IReadOnlyList<TrainingAutoAdaptationRecord>? recent = null, string id = "bench-press") =>
        AdaptationPlanner.Evaluate(new AdaptationInput(exposures, sessions, [id], recent ?? [], T0.AddDays(exposures.Count)));

    private static IReadOnlyList<SetRecord> Sets(decimal kg, int reps, int count = 3, decimal rir = 2) =>
        Enumerable.Range(0, count).Select(_ => Set(kg, reps, rir)).ToArray();

    [Fact]
    public void RepsInsideTheRangeKeepTheLoadAndAskForOneMoreRep()
    {
        var outcome = Run([Exposure(0, Sets(60, 8))], [Session(0)]);

        var advice = Assert.Single(outcome.Exercises);
        Assert.Equal(nameof(TrainingExerciseProgressionAction.AddRepetitions), advice.Action);
        Assert.Equal(60m, advice.NextLoadKg);
        Assert.Equal([9, 9, 9], advice.SuggestedRepetitions);
        Assert.Empty(outcome.Actions);
    }

    [Fact]
    public void TwoSessionsAtTheTopOfTheRangeRaiseTheLoadByOneStep_NeverMoreThanTheLimit()
    {
        var outcome = Run([Exposure(0, Sets(60, 10)), Exposure(3, Sets(60, 10))], [Session(0), Session(3)]);

        var advice = Assert.Single(outcome.Exercises);
        Assert.Equal(nameof(TrainingExerciseProgressionAction.IncreaseLoad), advice.Action);
        Assert.Equal(62.5m, advice.NextLoadKg);
        Assert.True(advice.NextLoadKg <= 60m * 1.05m + 0.01m);
    }

    [Fact]
    public void AGoodSessionIsNotEnoughToRaiseTheLoad()
    {
        var outcome = Run([Exposure(0, Sets(60, 8)), Exposure(3, Sets(60, 10))], [Session(0), Session(3)]);

        Assert.Equal(nameof(TrainingExerciseProgressionAction.ConfirmTopOfRange), Assert.Single(outcome.Exercises).Action);
        Assert.Equal(60m, outcome.Exercises[0].NextLoadKg);
    }

    [Fact]
    public void RepeatedFailureAtTheSameLoadLowersIt()
    {
        var failed = new[] { Set(80, 4), Set(80, 4), Set(80, 4) };

        var outcome = Run([Exposure(0, failed), Exposure(3, failed)], [Session(0), Session(3)]);

        var advice = Assert.Single(outcome.Exercises);
        Assert.Equal(nameof(TrainingExerciseProgressionAction.ReduceLoad), advice.Action);
        Assert.True(advice.NextLoadKg < 80m);
    }

    [Fact]
    public void PainIsNeverAutomated_ItPausesProgressionAndIsReported()
    {
        var sets = new[] { Set(60, 8), Set(60, 8, pain: true, skipped: "pain", done: false), Set(60, 8, done: false) };

        var outcome = Run([Exposure(0, sets, WorkoutOutcome.Partial)], [Session(0, WorkoutOutcome.Partial, pain: true)]);

        Assert.Empty(outcome.Actions);
        Assert.Equal(nameof(TrainingAdaptationAction.PauseAffectedExerciseAndReview), outcome.SessionAction);
        Assert.NotEmpty(outcome.Notes);
        Assert.Null(outcome.Exercises[0].NextLoadKg);
        Assert.Equal(nameof(TrainingExerciseProgressionAction.HoldAndReview), outcome.Exercises[0].Action);
    }

    [Fact]
    public void RepeatedLowRecoveryRaisesTheReserve_ButNotTwiceWithinTheCooldown()
    {
        var exposures = new[] { Exposure(0, Sets(60, 8), recovery: "Insufficient"), Exposure(2, Sets(60, 8), recovery: "Insufficient") };
        var sessions = new[] { Session(0, recovery: "Insufficient"), Session(2, recovery: "Insufficient") };

        var first = Run(exposures, sessions);
        var second = Run(exposures, sessions, [new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.RaiseReserve, T0.AddDays(1))]);

        Assert.Equal(TrainingAutoAdaptationKind.RaiseReserve, Assert.Single(first.Actions).Kind);
        Assert.DoesNotContain(second.Actions, a => a.Kind == TrainingAutoAdaptationKind.RaiseReserve);
        Assert.NotEmpty(second.Notes); // it says why nothing was applied
    }

    [Fact]
    public void ThreeStressedSessionsProposeADeload()
    {
        var stressed = new[] { Set(60, 8, done: true), Set(60, 6, done: false, skipped: "fatigue"), Set(60, 6, done: false, skipped: "fatigue") };
        var exposures = new[] { 0, 2, 4 }.Select(d => Exposure(d, stressed, WorkoutOutcome.Partial, "Insufficient")).ToArray();
        var sessions = new[] { 0, 2, 4 }.Select(d => Session(d, WorkoutOutcome.Partial, "Insufficient", "Fatigue", rpe: 10, completion: 0.4m)).ToArray();

        var outcome = Run(exposures, sessions);

        Assert.Contains(outcome.Actions, a => a.Kind == TrainingAutoAdaptationKind.Deload);
    }

    [Fact]
    public void RepeatedFatigueInOneExerciseRemovesOneSetThere()
    {
        var fatigue = new[] { Set(60, 8, rir: 0), Set(60, 8, rir: 0), Set(60, 8, rir: 0) }; // RIR far below the target of 2
        var exposures = new[] { 0, 2, 4 }.Select(d => Exposure(d, fatigue)).ToArray();

        var outcome = Run(exposures, [Session(0), Session(2), Session(4)]);

        var reduce = Assert.Single(outcome.Actions, a => a.Kind == TrainingAutoAdaptationKind.ReduceSets);
        Assert.Equal(["bench-press"], reduce.ExerciseIds);
    }

    [Fact]
    public void AReductionIsGivenBackAfterThreeCleanSessionsAtTheReducedDose()
    {
        var clean = Sets(60, 9, count: 2, rir: 3);
        var exposures = new[] { 0, 2, 4 }.Select(d => Exposure(d, clean, planned: 2, original: 3)).ToArray();

        var outcome = Run(exposures, [Session(0), Session(2), Session(4)]);

        Assert.Contains(outcome.Actions, a => a.Kind == TrainingAutoAdaptationKind.RestoreSets);
    }

    [Fact]
    public void DeloadSessionsNeverTriggerLoadProgression()
    {
        var outcome = Run([Exposure(0, Sets(60, 10), eligible: false), Exposure(3, Sets(60, 10), eligible: false)], [Session(0), Session(3)]);

        Assert.Equal(nameof(TrainingExerciseProgressionAction.HoldAndReview), Assert.Single(outcome.Exercises).Action);
    }

    /// <summary>
    /// Property: over random but plausible training histories the advice stays sane. A load never jumps by more than
    /// 10 %, never goes up after a failed or painful session, and is never zero or negative.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LoadAdviceOverRandomHistoriesIsMonotonicallyReasonable(int seed)
    {
        var random = new Random(seed);
        var history = new List<ExerciseExposureRecord>();
        var sessions = new List<SessionRecord>();
        var load = 40m + random.Next(0, 4) * 10m;
        var advised = 0;

        for (var day = 0; day < 30; day += 2)
        {
            var failed = random.NextDouble() < 0.25;
            var pain = random.NextDouble() < 0.1;
            var reps = failed ? random.Next(2, 6) : random.Next(6, 11);
            var sets = new[] { Set(load, reps, failed ? 0 : 2, pain: pain), Set(load, reps, failed ? 0 : 2), Set(load, reps, failed ? 0 : 2) };
            history.Add(Exposure(day, sets, failed ? WorkoutOutcome.Partial : WorkoutOutcome.Completed));
            sessions.Add(Session(day, failed ? WorkoutOutcome.Partial : WorkoutOutcome.Completed, pain: pain));

            var advice = AdaptationPlanner.Evaluate(new AdaptationInput(history, sessions, ["bench-press"], [], T0.AddDays(day)))
                .Exercises.Single();

            if (advice.NextLoadKg is { } next)
            {
                Assert.True(next > 0m);
                Assert.True(next <= load * 1.10m + 0.01m, $"jump from {load} to {next}");
                if (failed || pain) Assert.True(next <= load, $"load went up after a failed or painful session ({load} -> {next})");
                load = next;
            }
            if (advice.NextLoadKg is not null) advised++;
        }
        Assert.True(advised >= 10, "most sessions give a reference load");
    }
}
