using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// What a new plan does with the history of the user's earlier plans, on the real generator, observer and engine (no database).
/// Every test compares the first block of the new plan WITH the history against the same block WITHOUT it, so what is measured is
/// the difference the history makes in the sets the generator receives, never that a service was called.
/// </summary>
public sealed class CrossPlanLearningTests(ITestOutputHelper output)
{
    private static PlanProfile FourDays(params (TrainingMuscleGroup, TrainingMusclePriority)[] priorities) =>
        PlanTestCatalog.Profile(days: 4) with { MusclePriorities = priorities.ToDictionary(x => x.Item1, x => x.Item2) };

    private static int Structure(PlanProfile p) => p.SchedulingMode == Trazo.Domain.Planning.PlanSchedulingMode.Sequence ? p.SessionsPerPeriod : p.TrainingDays.Count;

    private static PlanSimulator Earlier(PlanProfile profile, Behavior behavior, int blocks)
    {
        var plan = new PlanSimulator(profile);
        for (var i = 0; i < blocks; i++) plan.RunBlock(behavior);
        return plan;
    }

    private static DateTimeOffset After(PlanSimulator plan, int days) => plan.Logs.Max(l => l.LoggedAt).AddDays(days);

    /// <summary>The longitudinal decision for the first block of <paramref name="next"/> as production takes it: all the user's observations, earlier plans first.</summary>
    private static LongitudinalResult FirstBlock(PlanSimulator next, DateTimeOffset now, params PlanSimulator[] earlier)
    {
        foreach (var plan in earlier) plan.RefreshObservations(now);
        var order = earlier.Select((e, i) => (Id: e.PlanId, Order: i)).Append((Id: next.PlanId, Order: earlier.Length)).ToDictionary(x => x.Id, x => x.Order);
        var cross = new CrossPlanContext(
            Structure(next.Profile), next.Profile.Split.ToString(), now,
            earlier.Select(e => new PlanHistory(e.PlanId, Structure(e.Profile), e.Profile.Split.ToString(), e.Baselines, e.Applied)).ToList());
        return LongitudinalEngine.Propose(next.PlanId, 1, next.Baselines, earlier.SelectMany(e => e.Observations).ToList(), order, [], cross);
    }

    private static IEnumerable<VolumeDecision> Carried(LongitudinalResult r) => r.Decisions.Where(d => d.Action == VolumeAction.Carry);
    private static decimal Offset(VolumeDecision d) => d.ProposedSets - d.BaselineSets;
    private static decimal NetOffset(LongitudinalResult r) => r.Decisions.Sum(Offset);

    private static readonly Behavior Healthy = new();
    private static readonly Behavior Struggling = new() { MuscleResponse = (_, _, _) => Response.Poor };

    private void Show(string label, LongitudinalResult r) => output.WriteLine(
        $"{label}: " + string.Join(", ", r.Decisions.Select(d => $"{d.Muscle} {d.BaselineSets:0}->{d.ProposedSets:0} ({d.Action}, conf {d.ConfidenceScore:0.00})")));

    // 1
    [Fact]
    public void A_user_without_history_gets_exactly_the_block_the_generator_made_before()
    {
        var next = new PlanSimulator(FourDays());
        var result = FirstBlock(next, PlanSimulator.Start.AddDays(40));
        Assert.Empty(result.Decisions);
        Assert.Empty(result.LearnedTargets);
        var plain = next.Generate(false, null);
        var learned = next.Generate(false, result.LearnedTargets);
        Assert.Equal(plain.Sessions.Select(s => s.PrescriptionJson), learned.Sessions.Select(s => s.PrescriptionJson));
    }

    // 2
    [Fact]
    public void A_favorable_history_of_several_blocks_raises_the_starting_reference_and_the_generated_sets()
    {
        var old = Earlier(FourDays(), Healthy, 4);
        var next = new PlanSimulator(FourDays());
        var result = FirstBlock(next, After(old, 14), old);
        Show("favorable", result);

        var raised = Carried(result).Where(d => d.ProposedSets > d.BaselineSets).ToList();
        Assert.NotEmpty(raised);
        Assert.All(result.Decisions, d =>
        {
            var b = next.Baselines.First(x => x.Muscle.ToString() == d.Muscle);
            Assert.InRange(d.ProposedSets, b.Floor, b.Ceiling);
            Assert.True(Offset(d) <= LongitudinalPolicy.MaximumCarriedIncreaseSets, $"{d.Muscle} starts {Offset(d)} sets above its baseline");
            Assert.Equal(LongitudinalPolicy.Version, "longitudinal-1.2");
            Assert.False(string.IsNullOrWhiteSpace(d.Reason));
        });
        // The difference reaches the block: the muscle that starts higher really gets more weekly sets than without the history.
        var withHistory = PlanSimulator.VolumeOfWeek(next.Generate(false, result.LearnedTargets));
        var without = PlanSimulator.VolumeOfWeek(next.Generate(false, null));
        var muscle = Enum.Parse<TrainingMuscleGroup>(raised[0].Muscle);
        Assert.True(withHistory[muscle] > without[muscle], $"{muscle}: {withHistory[muscle]} vs {without[muscle]}");
    }

    [Fact]
    public void One_favorable_block_is_not_enough_to_start_a_new_plan_higher()
    {
        var old = Earlier(FourDays(), Healthy, 1);
        var result = FirstBlock(new PlanSimulator(FourDays()), After(old, 14), old);
        Assert.DoesNotContain(result.Decisions, d => d.ProposedSets > d.BaselineSets);
        Assert.Empty(result.LearnedTargets);
    }

    // 3
    [Fact]
    public void A_poor_history_never_starts_the_new_plan_above_its_baseline()
    {
        var old = Earlier(FourDays(), Struggling, 3);
        var next = new PlanSimulator(FourDays());
        var result = FirstBlock(next, After(old, 14), old);
        Show("unfavorable", result);
        Assert.DoesNotContain(result.Decisions, d => d.ProposedSets > d.BaselineSets);
        Assert.Contains(Carried(result), d => d.ProposedSets < d.BaselineSets);
        Assert.All(result.Decisions, d => Assert.True(d.ProposedSets >= next.Baselines.First(b => b.Muscle.ToString() == d.Muscle).Floor));
    }

    [Fact]
    public void Pain_in_the_history_means_the_new_plan_starts_from_its_baseline()
    {
        var hurt = new Behavior { Pain = (_, _) => true };
        var old = Earlier(FourDays(), hurt, 3);
        var result = FirstBlock(new PlanSimulator(FourDays()), After(old, 14), old);
        Assert.DoesNotContain(result.Decisions, d => d.ProposedSets != d.BaselineSets);
    }

    // 4
    [Fact]
    public void The_weight_of_the_history_fades_with_its_age_and_old_history_is_ignored()
    {
        var old = Earlier(FourDays(), Healthy, 4);
        var offsets = new List<(int Days, decimal Net, decimal Confidence)>();
        foreach (var days in new[] { 7, 70, 110, 160, 400 })
        {
            var result = FirstBlock(new PlanSimulator(FourDays()), After(old, days), old);
            offsets.Add((days, NetOffset(result), result.Decisions.Select(d => d.ConfidenceScore).DefaultIfEmpty(0m).Max()));
            output.WriteLine($"{days} days: net {offsets[^1].Net}, max confidence {offsets[^1].Confidence}");
        }
        Assert.True(offsets[0].Net > 0m);
        for (var i = 1; i < offsets.Count; i++)
        {
            Assert.True(offsets[i].Net <= offsets[i - 1].Net, $"history of {offsets[i].Days} days weighs more than the one of {offsets[i - 1].Days}");
            Assert.True(offsets[i].Confidence <= offsets[i - 1].Confidence);
        }
        Assert.True(offsets[^1].Net == 0m, "a history older than the expiry must not move the reference");
        var ancient = FirstBlock(new PlanSimulator(FourDays()), After(old, 400), old);
        Assert.Empty(ancient.LearnedTargets);
    }

    // 5
    [Fact]
    public void Coming_back_after_a_long_break_returns_to_conservative_values_but_a_past_reduction_is_remembered_longer()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var poor = Earlier(FourDays(), Struggling, 3);
        var afterBreak = 120;
        var goodResult = FirstBlock(new PlanSimulator(FourDays()), After(good, afterBreak), good);
        var poorResult = FirstBlock(new PlanSimulator(FourDays()), After(poor, afterBreak), poor);
        Show("good after break", goodResult);
        Show("poor after break", poorResult);
        // Whatever increase there was does not survive a long break; caution about a poor response does (it can only lower the dose).
        Assert.DoesNotContain(goodResult.Decisions, d => d.ProposedSets > d.BaselineSets);
        Assert.DoesNotContain(poorResult.Decisions, d => d.ProposedSets > d.BaselineSets);
        Assert.True(NetOffset(poorResult) <= 0m);
        // The block still gets the longer break flagged as such in the explanation of every inherited change.
        Assert.All(Carried(poorResult), d => Assert.Contains("Hace ", d.Reason));
    }

    // 6
    [Fact]
    public void A_history_from_another_goal_is_not_an_earlier_plan_to_learn_from()
    {
        var old = Earlier(FourDays(), Healthy, 4);
        old.RefreshObservations(After(old, 14));
        var next = new PlanSimulator(FourDays());
        // The plan history the infrastructure builds only holds generated hypertrophy plans: a strength plan is simply not in it.
        var cross = new CrossPlanContext(4, next.Profile.Split.ToString(), After(old, 14), []);
        var result = LongitudinalEngine.Propose(
            next.PlanId, 1, next.Baselines, old.Observations, new Dictionary<Guid, int> { [old.PlanId] = 0, [next.PlanId] = 1 }, [], cross);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.LearnedTargets);
    }

    // 7
    [Fact]
    public void A_gain_earned_with_four_days_is_not_carried_to_three_days_but_a_reduction_is()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var threeDays = new PlanSimulator(PlanTestCatalog.Profile(days: 3));
        var same = FirstBlock(new PlanSimulator(FourDays()), After(good, 14), good);
        var changed = FirstBlock(threeDays, After(good, 14), good);
        Show("same frequency", same);
        Show("three days", changed);
        Assert.True(NetOffset(same) > 0m);
        Assert.DoesNotContain(changed.Decisions, d => d.ProposedSets > d.BaselineSets);
        Assert.All(changed.Decisions, d => Assert.Contains("estructura semanal", d.Reason));

        var poor = Earlier(FourDays(), Struggling, 3);
        var cautious = FirstBlock(new PlanSimulator(PlanTestCatalog.Profile(days: 3)), After(poor, 14), poor);
        Assert.Contains(cautious.Decisions, d => d.ProposedSets < d.BaselineSets);
    }

    [Fact]
    public void History_from_other_conditions_cannot_complete_the_two_favorable_blocks_of_the_new_plan()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var plan = new PlanSimulator(PlanTestCatalog.Profile(days: 3));
        var now = After(good, 14);
        var first = FirstBlock(plan, now, good);
        foreach (var d in first.Decisions) plan.Applied.Add(new AppliedTarget(1, d.Muscle, d.ProposedSets));
        plan.Train(plan.Generate(false, first.LearnedTargets), Healthy); // ONE favorable block of the new plan
        plan.RefreshObservations(now.AddDays(28));
        var order = new Dictionary<Guid, int> { [good.PlanId] = 0, [plan.PlanId] = 1 };
        var cross = new CrossPlanContext(3, plan.Profile.Split.ToString(), now.AddDays(28), [
            new PlanHistory(good.PlanId, 4, good.Profile.Split.ToString(), good.Baselines, good.Applied)]);
        var second = LongitudinalEngine.Propose(plan.PlanId, 2, plan.Baselines, good.Observations.Concat(plan.Observations).ToList(), order, plan.Applied, cross);
        Show("second block, other structure", second);
        Assert.DoesNotContain(second.Decisions, d => d.Action == VolumeAction.Increase);
        Assert.DoesNotContain(second.Decisions, d => d.ProposedSets > d.BaselineSets);
    }

    [Fact]
    public void A_change_of_split_counts_as_a_change_of_weekly_structure()
    {
        var good = Earlier(PlanTestCatalog.Profile(days: 4, split: TrainingProgramSplit.UpperLower), Healthy, 4);
        var other = new PlanSimulator(PlanTestCatalog.Profile(days: 4, split: TrainingProgramSplit.FullBody));
        var result = FirstBlock(other, After(good, 14), good);
        Assert.DoesNotContain(result.Decisions, d => d.ProposedSets > d.BaselineSets);
    }

    // 8
    [Fact]
    public void A_change_of_muscle_priorities_keeps_the_new_priorities_and_drops_only_what_depended_on_the_old_ones()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var emphasizedChest = new PlanSimulator(FourDays((TrainingMuscleGroup.Chest, TrainingMusclePriority.Emphasize)));
        var result = FirstBlock(emphasizedChest, After(good, 14), good);
        var plain = FirstBlock(new PlanSimulator(FourDays()), After(good, 14), good);
        Show("chest emphasized", result);

        var chest = result.Decisions.FirstOrDefault(d => d.Muscle == "Chest");
        Assert.True(chest is null || chest.ProposedSets <= chest.BaselineSets, "the gain earned at normal priority is not carried to an emphasized chest");
        // The priority of the new plan is still what sets the baseline.
        Assert.True(emphasizedChest.Baselines.First(b => b.Muscle == TrainingMuscleGroup.Chest).Target
                    > new PlanSimulator(FourDays()).Baselines.First(b => b.Muscle == TrainingMuscleGroup.Chest).Target);
        // Muscles whose priority did not change keep what they earned.
        foreach (var d in plain.Decisions.Where(x => x.Muscle != "Chest" && x.ProposedSets > x.BaselineSets))
            Assert.Equal(d.ProposedSets - d.BaselineSets, result.Decisions.First(x => x.Muscle == d.Muscle).ProposedSets - d.BaselineSets);
    }

    [Fact]
    public void A_muscle_in_maintenance_in_the_new_plan_never_starts_above_its_baseline()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var maintained = new PlanSimulator(FourDays((TrainingMuscleGroup.Chest, TrainingMusclePriority.Maintain)));
        var result = FirstBlock(maintained, After(good, 14), good);
        Assert.DoesNotContain(result.Decisions.Where(d => d.Muscle == "Chest"), d => d.ProposedSets > d.BaselineSets);
    }

    // 9
    [Fact]
    public void A_different_plan_with_comparable_conditions_inherits_the_same_reference()
    {
        var good = Earlier(FourDays(), Healthy, 4);
        var a = FirstBlock(new PlanSimulator(FourDays(), seed: 7), After(good, 14), good);
        var b = FirstBlock(new PlanSimulator(FourDays(), seed: 99), After(good, 14), good);
        Assert.Equal(a.LearnedTargets.OrderBy(x => x.Key), b.LearnedTargets.OrderBy(x => x.Key));
        Assert.True(a.LearnedTargets.Count > 0);
    }

    // 10 (the engine only ever sees what the infrastructure loads for one user; the SQL side is tested in the API scenarios)
    [Fact]
    public void Another_users_history_is_not_something_a_plan_can_ask_for()
    {
        var theirs = Earlier(FourDays(), Healthy, 4);
        var mine = new PlanSimulator(FourDays());
        // Same engine call as production for a user whose own history is empty: the other user's rows are simply not in the input.
        var result = FirstBlock(mine, After(theirs, 14));
        Assert.Empty(result.Decisions);
        Assert.Empty(result.LearnedTargets);
    }

    // 11
    [Fact]
    public void Deciding_the_same_block_twice_gives_the_same_decisions()
    {
        var old = Earlier(FourDays(), Healthy, 4);
        var next = new PlanSimulator(FourDays());
        var now = After(old, 14);
        var first = FirstBlock(next, now, old);
        var second = FirstBlock(next, now, old);
        Assert.Equal(first.Decisions.Select(d => (d.Muscle, d.Action, d.ProposedSets, d.Reason)), second.Decisions.Select(d => (d.Muscle, d.Action, d.ProposedSets, d.Reason)));
        Assert.Equal(first.LearnedTargets.OrderBy(x => x.Key), second.LearnedTargets.OrderBy(x => x.Key));
    }

    [Fact]
    public void The_inherited_reference_is_the_starting_point_and_the_new_plans_own_data_still_decide_what_follows()
    {
        var old = Earlier(FourDays(), Healthy, 4);
        var plan = new PlanSimulator(FourDays());
        var first = FirstBlock(plan, After(old, 14), old);
        var inherited = Carried(first).First(d => d.ProposedSets > d.BaselineSets);
        var muscle = Enum.Parse<TrainingMuscleGroup>(inherited.Muscle);

        // Block 1 of the new plan is generated with the inherited reference and recorded as applied.
        foreach (var d in first.Decisions) plan.Applied.Add(new AppliedTarget(1, d.Muscle, d.ProposedSets));
        plan.Train(plan.Generate(false, first.LearnedTargets), Struggling);
        // The user does badly with it: the next block goes DOWN from the inherited reference, whatever the old plan said.
        plan.RefreshObservations(PlanSimulator.Start.AddDays(7 * plan.NextWeek));
        var order = new Dictionary<Guid, int> { [old.PlanId] = 0, [plan.PlanId] = 1 };
        var cross = new CrossPlanContext(4, plan.Profile.Split.ToString(), After(old, 14).AddDays(28), [
            new PlanHistory(old.PlanId, 4, old.Profile.Split.ToString(), old.Baselines, old.Applied)]);
        var second = LongitudinalEngine.Propose(plan.PlanId, 2, plan.Baselines, old.Observations.Concat(plan.Observations).ToList(), order, plan.Applied, cross);
        var next = second.Decisions.First(d => d.Muscle == inherited.Muscle);
        output.WriteLine($"{muscle}: baseline {inherited.BaselineSets}, inherited {inherited.ProposedSets}, next {next.ProposedSets} ({next.Action})");
        Assert.True(next.ProposedSets <= inherited.ProposedSets);
    }
}
