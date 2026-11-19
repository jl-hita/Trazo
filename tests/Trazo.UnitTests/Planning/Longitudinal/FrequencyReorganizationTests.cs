using Trazo.Application.Planning;
using Trazo.Application.Planning.Frequency;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// Scenario 13: the days the user really trains against the days the plan was built for. The detector tells an irregular week from a
/// persistent change; the reorganizer turns a persistent change into a plan for the days the user has; the generator then builds the next block
/// for those days. Nothing already generated or logged is touched, and the user can come back.
/// </summary>
public sealed class FrequencyReorganizationTests
{
    private const TrainingMuscleGroup Chest = TrainingMuscleGroup.Chest;
    private static readonly DateOnly Anchor = new(2026, 11, 16);
    private static readonly DayOfWeek[] Mon4 = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday];

    private static WeeklyAttendance Week(int index, int days, bool deload = false) =>
        new(Anchor.AddDays(7 * index), Mon4.Take(days).ToList(), deload);

    private static List<WeeklyAttendance> Weeks(params int[] days) => days.Select((d, i) => Week(i, d)).ToList();

    // ---------------------------------------------------------------- detector

    [Fact]
    public void Four_planned_and_four_done_is_on_track()
    {
        var a = FrequencyPatternDetector.Assess(4, null, Weeks(4, 4, 4, 4, 4, 4));
        Assert.Equal(FrequencyPattern.OnTrack, a.Pattern);
        Assert.Null(a.TargetDays);
    }

    [Theory]
    [InlineData(new[] { 4, 4, 4, 4, 3, 4 })]
    [InlineData(new[] { 4, 4, 3, 4, 3, 4 })]
    [InlineData(new[] { 4, 4, 4, 3, 3, 4 })]
    public void A_few_irregular_weeks_are_not_a_change(int[] days)
    {
        var a = FrequencyPatternDetector.Assess(4, null, Weeks(days));
        Assert.Equal(FrequencyPattern.Transient, a.Pattern);
        Assert.Null(a.TargetDays);
    }

    [Fact]
    public void Three_days_for_several_weeks_is_a_persistent_reduction()
    {
        var a = FrequencyPatternDetector.Assess(4, null, Weeks(4, 4, 3, 3, 3, 3));
        Assert.Equal(FrequencyPattern.PersistentReduction, a.Pattern);
        Assert.Equal(3, a.TargetDays);
        Assert.False(string.IsNullOrWhiteSpace(a.Reason));
    }

    [Fact]
    public void A_pattern_that_already_ended_is_not_a_current_availability()
    {
        var a = FrequencyPatternDetector.Assess(4, null, Weeks(4, 3, 3, 3, 4, 4));
        Assert.NotEqual(FrequencyPattern.PersistentReduction, a.Pattern);
    }

    [Fact]
    public void Erratic_weeks_are_not_a_pace()
    {
        var a = FrequencyPatternDetector.Assess(4, null, Weeks(4, 3, 1, 2, 3, 1));
        Assert.NotEqual(FrequencyPattern.PersistentReduction, a.Pattern);
    }

    [Fact]
    public void Too_few_weeks_say_nothing()
    {
        Assert.Equal(FrequencyPattern.Insufficient, FrequencyPatternDetector.Assess(4, null, Weeks(3, 3, 3)).Pattern);
    }

    [Fact]
    public void Absent_weeks_and_deload_weeks_are_not_evidence()
    {
        // Two weeks without any workout: an absence, not a new availability.
        Assert.Equal(FrequencyPattern.Insufficient, FrequencyPatternDetector.Assess(4, null, Weeks(4, 0, 0, 3, 3, 3)).Pattern);
        // Deload weeks (training less on purpose) are skipped, so the 2 days there do not count as low weeks.
        var weeks = new List<WeeklyAttendance> { Week(0, 4), Week(1, 4), Week(2, 4), Week(3, 4), Week(4, 2, deload: true), Week(5, 4) };
        Assert.Equal(FrequencyPattern.OnTrack, FrequencyPatternDetector.Assess(4, null, weeks).Pattern);
    }

    [Fact]
    public void Never_proposes_fewer_than_two_days()
    {
        var a = FrequencyPatternDetector.Assess(3, null, Weeks(1, 1, 1, 1, 1, 1));
        Assert.Equal(FrequencyPattern.PersistentReduction, a.Pattern);
        Assert.Equal(FrequencyPolicy.MinimumDaysPerWeek, a.TargetDays);
    }

    [Fact]
    public void Back_to_the_original_days_is_recognised_only_after_a_reorganisation()
    {
        var weeks = Weeks(4, 4, 4, 4);
        var plain = FrequencyPatternDetector.Assess(3, null, weeks);
        Assert.NotEqual(FrequencyPattern.PersistentReturn, plain.Pattern);
        var back = FrequencyPatternDetector.Assess(3, 4, weeks);
        Assert.Equal(FrequencyPattern.PersistentReturn, back.Pattern);
        Assert.Equal(4, back.TargetDays);
        // Training even more never goes past the days the plan had.
        Assert.Equal(4, FrequencyPatternDetector.Assess(3, 4, Weeks(5, 5, 5, 5)).TargetDays);
    }

    [Fact]
    public void Weeks_are_built_from_complete_plan_weeks_only()
    {
        var days = new[] { Anchor, Anchor.AddDays(2), Anchor.AddDays(7), Anchor.AddDays(14) };
        var weeks = FrequencyPatternDetector.Weeks(days, Anchor.AddDays(17), Anchor, Anchor, _ => false);
        Assert.Equal(2, weeks.Count); // the third week (days 14-20) is still in progress on day 17
        Assert.Equal(2, weeks[0].Sessions);
        Assert.Equal(1, weeks[1].Sessions);
    }

    // ---------------------------------------------------------------- choosing the days

    [Fact]
    public void The_days_follow_the_weekdays_the_user_really_trains()
    {
        var weeks = Enumerable.Range(0, 4).Select(i => new WeeklyAttendance(
            Anchor.AddDays(7 * i), [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Sunday], false)).ToList();
        var days = FrequencyReorganizer.ChooseDays(3, weeks, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday], null);
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Sunday], days);
    }

    [Fact]
    public void Three_days_are_never_three_consecutive_days()
    {
        var weeks = Enumerable.Range(0, 4).Select(i => new WeeklyAttendance(
            Anchor.AddDays(7 * i), [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday], false)).ToList();
        var days = FrequencyReorganizer.ChooseDays(3, weeks, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday], null);
        var order = days.Select(FrequencyPatternDetector.Order).OrderBy(x => x).ToList();
        Assert.False(order[1] == order[0] + 1 && order[2] == order[1] + 1, "three consecutive days were chosen");
        Assert.Equal(3, days.Count);
    }

    // ---------------------------------------------------------------- scenario 13 end to end

    private static readonly Behavior ThreeOfFour = new() { Attends = (_, index) => index < 3 };

    private static PlanProfile Profile() => PlanTestCatalog.Profile(days: 4) with
    {
        MusclePriorities = new Dictionary<TrainingMuscleGroup, TrainingMusclePriority> { [Chest] = TrainingMusclePriority.Emphasize }
    };

    private static decimal Total(IReadOnlyDictionary<TrainingMuscleGroup, decimal> v) => v.Values.Sum();

    [Fact]
    public void Scenario13_four_planned_four_done_makes_no_proposal()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(new Behavior());
        Assert.Equal(FrequencyPattern.OnTrack, s.AssessFrequency().Pattern);
        Assert.Null(s.ProposeFrequency());
    }

    [Fact]
    public void Scenario13_three_days_once_is_an_irregular_week_and_changes_nothing()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(new Behavior { Attends = (period, index) => !(period == 7 && index == 3) });
        Assert.Equal(FrequencyPattern.Transient, s.AssessFrequency().Pattern);
        Assert.Null(s.ProposeFrequency());
        Assert.Equal(4, s.Profile.TrainingDays.Count);
    }

    [Fact]
    public void Scenario13_three_days_for_weeks_proposes_a_three_day_plan_that_fits_the_user()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(ThreeOfFour);
        var assessment = s.AssessFrequency();
        Assert.Equal(FrequencyPattern.PersistentReduction, assessment.Pattern);
        var draft = s.ProposeFrequency(assessment);
        Assert.NotNull(draft);
        Assert.Equal(FrequencyDraft.Reduce, draft!.Kind);
        // The user trained Monday, Wednesday and Friday: those are the days proposed.
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], draft.ToDays);
        Assert.Equal(3, draft.Preview.ProposedSessionsPerWeek);
        Assert.Contains("no es algo puntual", draft.Reason);
        Assert.Contains("no se acumula", draft.Reason);
        // No longer sessions to make up for the missing day.
        Assert.True(draft.Preview.ProposedLongestSessionMinutes <= s.Profile.MaxSessionMinutes);
        // Sessions as long as they are, not longer (a little room), and the weekly volume follows the time the user really has.
        Assert.True(draft.Preview.ProposedAverageSessionMinutes <= draft.Preview.CurrentAverageSessionMinutes * 1.15m,
            $"average {draft.Preview.CurrentAverageSessionMinutes} -> {draft.Preview.ProposedAverageSessionMinutes}");
        Assert.True(draft.Preview.ProposedLongestSessionMinutes <= draft.Preview.ProposedMaxSessionMinutes);
        // No attempt to recover the lost volume: in total the three-day block asks for no more than the four-day one, and no muscle jumps.
        var currentTotal = draft.Preview.Muscles.Sum(m => m.CurrentSets);
        var proposedTotal = draft.Preview.Muscles.Sum(m => m.ProposedSets);
        Assert.True(proposedTotal <= currentTotal, $"total {currentTotal} -> {proposedTotal}");
        Assert.All(draft.Preview.Muscles, m => Assert.True(m.ProposedSets <= m.CurrentSets + 3m, $"{m.Muscle} {m.CurrentSets} -> {m.ProposedSets}"));
        // Good frequency of stimulus: the main muscles are still worked twice a week.
        foreach (var m in draft.Preview.Muscles.Where(m => m.Muscle is "Chest" or "Lats" or "Quadriceps")) Assert.True(m.ProposedFrequency >= 2, $"{m.Muscle} {m.ProposedFrequency}");
    }

    [Fact]
    public void Scenario13_accepting_generates_the_next_block_with_three_days_without_touching_what_exists()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(ThreeOfFour);
        var oldBlock = s.Generate(false, null);
        var draft = s.ProposeFrequency()!;
        var sessions = s.Sessions.Count;
        var logs = s.Logs.Count;
        var observations = s.Observations.Count;
        var pending = s.Sessions.Count(x => x.Status == Trazo.Domain.Planning.PlannedSessionStatus.Planned);
        Assert.True(pending > 0, "the skipped sessions stay as they are (not performed)");

        s.AcceptFrequency(draft);
        Assert.Equal(sessions, s.Sessions.Count);
        Assert.Equal(logs, s.Logs.Count);
        Assert.Equal(observations, s.Observations.Count);
        Assert.Equal(pending, s.Sessions.Count(x => x.Status == Trazo.Domain.Planning.PlannedSessionStatus.Planned));

        var next = s.Decide();
        var block = s.Generate(false, next.LearnedTargets);
        Assert.Equal(12, block.Sessions.Count); // 3 days x 4 weeks: the skipped sessions are not added on top
        Assert.All(block.Sessions.GroupBy(x => x.WeekNumber), g => Assert.Equal(3, g.Count()));
        Assert.All(block.Sessions, x => Assert.True(x.EstimatedDurationMinutes <= s.Profile.MaxSessionMinutes));
        var oldVolume = PlanSimulator.VolumeOfWeek(oldBlock);
        var newVolume = PlanSimulator.VolumeOfWeek(block);
        Assert.True(Total(newVolume) <= Total(oldVolume), $"total {Total(oldVolume)} -> {Total(newVolume)}");
        Assert.All(newVolume, v => Assert.True(v.Value <= oldVolume.GetValueOrDefault(v.Key) + 3m, $"{v.Key} {oldVolume.GetValueOrDefault(v.Key)} -> {v.Value}"));
        // Sessions as long as they were: the plan's own maximum was lowered to the current ones, never raised.
        Assert.True(s.Profile.MaxSessionMinutes <= 75);
        // The emphasised muscle keeps its priority at three days.
        Assert.Equal(TrainingMusclePriority.Emphasize, s.Baselines.First(b => b.Muscle == Chest).Priority);
        Assert.True(newVolume[Chest] >= s.Baselines.First(b => b.Muscle == Chest).Target - 2m);
        // The learned doses stay inside the band of the NEW baselines: a reference earned at four days cannot carry over.
        Assert.All(next.Decisions, d => Assert.InRange(d.ProposedSets, d.BaselineSets - LongitudinalPolicy.MaximumBelowBaselineSets, d.BaselineSets));
    }

    [Fact]
    public void Scenario13_the_reorganised_plan_matches_the_availability_better_than_keeping_four_days()
    {
        PlanSimulator Prepare()
        {
            var sim = new PlanSimulator(Profile());
            sim.RunBlock(new Behavior());
            sim.RunBlock(ThreeOfFour);
            return sim;
        }

        static (decimal Prescribed, decimal Performed) Block(PlanSimulator sim)
        {
            sim.RefreshObservations(sim.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var rows = sim.Observations.Where(o => o.Period > 8).ToList();
            return (rows.Sum(o => o.PrescribedSets), rows.Sum(o => o.PerformedSets));
        }

        var reorganised = Prepare();
        reorganised.AcceptFrequency(reorganised.ProposeFrequency()!);
        reorganised.RunBlock(ThreeOfFour);
        var kept = Prepare();
        kept.RunBlock(ThreeOfFour);

        var (rPrescribed, rPerformed) = Block(reorganised);
        var (kPrescribed, kPerformed) = Block(kept);
        var rGap = 1m - rPerformed / rPrescribed;
        var kGap = 1m - kPerformed / kPrescribed;
        // Keeping four days prescribes work the user does not do; three days prescribes what the user does.
        Assert.True(kGap > 0.15m, $"kept gap {kGap}");
        Assert.True(rGap < 0.05m, $"reorganised gap {rGap}");
        // And the user does not lose volume by it: what is done is about the same or more.
        Assert.True(rPerformed >= kPerformed * 0.85m, $"{rPerformed} vs {kPerformed}");
    }

    [Fact]
    public void Scenario13_coming_back_to_four_days_restores_the_original_plan_without_inflating_volume()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(ThreeOfFour);
        var original = s.Profile.TrainingDays;
        var control = new PlanSimulator(Profile());
        control.RunBlock(new Behavior());
        control.RunBlock(ThreeOfFour);
        control.RunBlock(ThreeOfFour);
        s.AcceptFrequency(s.ProposeFrequency()!);
        s.RunBlock(ThreeOfFour);
        Assert.Equal(original, s.OriginalDays);

        // The user trains four days again for several weeks (pulling sessions forward is not modelled by the simulator, so the weeks are given).
        var weeks = Enumerable.Range(0, 4).Select(i => new WeeklyAttendance(s.Today.AddDays(-28 + 7 * i), Mon4, false)).ToList();
        var assessment = FrequencyPatternDetector.Assess(s.Profile.TrainingDays.Count, s.OriginalDays!.Count, weeks);
        Assert.Equal(FrequencyPattern.PersistentReturn, assessment.Pattern);
        var draft = s.ProposeFrequency(assessment, weeks);
        Assert.NotNull(draft);
        Assert.Equal(FrequencyDraft.Restore, draft!.Kind);
        Assert.Equal(original.OrderBy(FrequencyPatternDetector.Order), draft.ToDays);

        s.AcceptFrequency(draft);
        Assert.Null(s.OriginalDays);
        var restored = s.Generate(false, null);
        Assert.Equal(16, restored.Sessions.Count);
        // The volume goes back to what a four-day plan asks for at this point (the exercises differ a little because the previous block did),
        // never to more: nothing of the skipped sessions is added on top.
        var restoredTotal = Total(PlanSimulator.VolumeOfWeek(restored));
        var controlTotal = Total(PlanSimulator.VolumeOfWeek(control.Generate(false, null)));
        Assert.InRange(restoredTotal, controlTotal * 0.8m, controlTotal * 1.2m);
        Assert.Equal(75, s.Profile.MaxSessionMinutes); // the limit the user had comes back with the days
    }

    [Fact]
    public void Scenario13_the_user_can_revert_without_losing_history()
    {
        var s = new PlanSimulator(Profile());
        s.RunBlock(new Behavior());
        s.RunBlock(ThreeOfFour);
        var before = s.Generate(false, null);
        s.AcceptFrequency(s.ProposeFrequency()!);
        var sessions = s.Sessions.Count;
        var logs = s.Logs.Count;
        var observations = s.Observations.Count;
        var applied = s.Applied.Count;

        s.RevertFrequency();
        Assert.Equal(4, s.Profile.TrainingDays.Count);
        Assert.Equal(75, s.Profile.MaxSessionMinutes);
        Assert.Equal(sessions, s.Sessions.Count);
        Assert.Equal(logs, s.Logs.Count);
        Assert.Equal(observations, s.Observations.Count);
        Assert.Equal(applied, s.Applied.Count);
        Assert.Equal(before.Sessions.Select(x => x.PrescriptionJson), s.Generate(false, null).Sessions.Select(x => x.PrescriptionJson));
    }
}
