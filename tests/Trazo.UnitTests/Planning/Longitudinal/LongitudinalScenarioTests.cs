using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// End-to-end scenarios of the longitudinal layer on the real generator, observer and engine (no database). What they check is the
/// behaviour the product wants, not a model of physiology: a better performance never raises a dose, repeated fatigue with falling
/// performance lowers it a little, a reduction is given back, pain always prevails and not knowing changes nothing.
/// The simulated "response" is an input of each scenario, so these tests prove how the engine REACTS to a signal, not that the reaction
/// improves anybody's hypertrophy: that needs real data and is not claimed here.
/// </summary>
public sealed class LongitudinalScenarioTests
{
    private const TrainingMuscleGroup Chest = TrainingMuscleGroup.Chest;
    private const TrainingMuscleGroup Triceps = TrainingMuscleGroup.Triceps;
    private const TrainingMuscleGroup Quads = TrainingMuscleGroup.Quadriceps;

    private static PlanProfile With(PlanProfile p, params (TrainingMuscleGroup, TrainingMusclePriority)[] priorities) =>
        p with { MusclePriorities = priorities.ToDictionary(x => x.Item1, x => x.Item2) };

    private static decimal Base(PlanSimulator s, TrainingMuscleGroup m) => s.Baselines.First(b => b.Muscle == m).Target;

    private static List<LongitudinalResult> Run(PlanSimulator s, Behavior b, int blocks)
    {
        var results = new List<LongitudinalResult>();
        for (var i = 0; i < blocks; i++) results.Add(s.RunBlock(b).Result);
        return results;
    }

    private static List<VolumeDecision> Of(IEnumerable<LongitudinalResult> results, TrainingMuscleGroup m) =>
        results.SelectMany(r => r.Decisions).Where(d => d.Muscle == m.ToString()).ToList();

    private static readonly Behavior ChestFatigues = new()
    {
        MuscleResponse = (_, muscle, _) => muscle == Chest ? Response.Poor : Response.Good
    };

    [Theory]
    [InlineData(TrainingExperience.Beginner, 3)]
    [InlineData(TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingExperience.Advanced, 4)]
    public void A_regular_user_with_good_performance_never_gets_more_than_the_plain_baseline(TrainingExperience level, int days)
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(experience: level, days: days));
        var results = Run(s, new Behavior(), 5);
        Assert.All(results.SelectMany(r => r.Decisions), d =>
        {
            Assert.True(d.ProposedSets <= d.BaselineSets, $"{d.Muscle}: {d.ProposedSets} over the baseline {d.BaselineSets}");
            Assert.NotEqual(VolumeAction.Decrease, d.Action);
        });
        Assert.All(results, r => Assert.Empty(r.LearnedTargets));
    }

    [Fact]
    public void A_learned_reduction_lowers_that_muscle_and_no_muscle_exceeds_the_absolute_ceiling()
    {
        // The allocator is greedy: lowering the target of one muscle can change which exercises get sets first, so another muscle may
        // land somewhere else INSIDE its own range (it is not a spill-over of volume). What a learned target must never do is raise
        // the muscle it is about, or take any muscle past the absolute weekly ceiling the same plan without learning respects.
        var learning = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var control = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var lowered = false;
        for (var block = 0; block < 5; block++)
        {
            var (learnedBlock, result) = learning.RunBlock(ChestFatigues);
            var plain = PlanSimulator.VolumeOfWeek(control.RunBlock(ChestFatigues, learn: false).Block);
            var learnedVolume = PlanSimulator.VolumeOfWeek(learnedBlock);
            foreach (var muscle in result.LearnedTargets.Keys)
            {
                lowered = true;
                Assert.True(learnedVolume.GetValueOrDefault(muscle) <= plain.GetValueOrDefault(muscle) + 0.01m,
                    $"block {block + 1}, {muscle}: {learnedVolume.GetValueOrDefault(muscle)} with a lowered target against {plain.GetValueOrDefault(muscle)} without");
            }
            Assert.All(learnedVolume, p => Assert.True(p.Value <= 20m, $"{p.Key}: {p.Value}"));
        }
        Assert.True(lowered, "the scenario must actually lower something");
    }

    [Theory]
    [InlineData(TrainingExperience.Beginner, 3)]
    [InlineData(TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingExperience.Advanced, 4)]
    [InlineData(TrainingExperience.Intermediate, 3)]
    public void A_learned_target_does_not_change_the_rules_the_same_muscle_follows_without_it(TrainingExperience level, int days)
    {
        // The generator is run directly: every configurable muscle gets a learned target at its floor (the largest reduction possible).
        var profile = PlanTestCatalog.Profile(experience: level, days: days);
        var baselines = MesocycleGenerator.Baselines(profile);
        var learned = baselines.ToDictionary(b => b.Muscle, b => b.Floor);
        var slot = new MesocycleSlot(1, 1, "accumulation", 1, 4);
        var plain = MesocycleGenerator.Generate(profile, 7, slot, PlanSimulator.Catalog);
        var lowered = MesocycleGenerator.Generate(profile, 7, slot, PlanSimulator.Catalog, learnedTargets: learned);
        Assert.True(plain.Succeeded, string.Join("; ", plain.Errors));
        Assert.True(lowered.Succeeded, string.Join("; ", lowered.Errors));
        for (var week = 1; week <= 4; week++)
        {
            var before = PlanSimulator.VolumeOfWeek(plain, week);
            var after = PlanSimulator.VolumeOfWeek(lowered, week);
            // The greedy allocator may land a muscle one or two sets elsewhere inside its own range when another target changes, so the
            // total is what must go down; what must never happen is a muscle past the ceiling the plain plan respects.
            Assert.True(baselines.Sum(b => after.GetValueOrDefault(b.Muscle)) <= baselines.Sum(b => before.GetValueOrDefault(b.Muscle)),
                $"{level} {days}d week {week}: lowering every target must not add volume overall");
            foreach (var b in baselines)
                Assert.True(after.GetValueOrDefault(b.Muscle) <= Math.Max(20m, before.GetValueOrDefault(b.Muscle)),
                    $"{level} {days}d week {week} {b.Muscle}: {after.GetValueOrDefault(b.Muscle)} lowered, {before.GetValueOrDefault(b.Muscle)} plain: over the ceiling");
        }
    }

    [Fact]
    public void Repeated_fatigue_with_falling_performance_lowers_that_muscle_by_one_set_per_block_inside_the_band()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, ChestFatigues, 6);
        var chest = Of(results, Chest);
        Assert.Contains(chest, d => d.Action == VolumeAction.Decrease);
        var baseline = s.Baselines.First(b => b.Muscle == Chest);
        Assert.All(chest, d =>
        {
            Assert.InRange(d.ProposedSets, Math.Max(baseline.Floor, baseline.Target - LongitudinalPolicy.MaximumBelowBaselineSets), baseline.Target);
            Assert.True(Math.Abs(d.ProposedSets - d.PreviousSets) <= LongitudinalPolicy.StepSets);
            Assert.False(string.IsNullOrWhiteSpace(d.Reason));
        });
        Assert.True(Of(results, Chest).Last().ProposedSets < Base(s, Chest));
    }

    [Fact]
    public void A_synergist_does_not_inherit_the_decline_of_a_lift_it_only_assists()
    {
        // Chest performance falls and the sessions are tiring, but the triceps' own lifts keep improving: the bench press is not
        // evidence about the triceps, so the triceps are not lowered because of it.
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, ChestFatigues, 5);
        Assert.Contains(Of(results, Chest), d => d.Action == VolumeAction.Decrease);
        Assert.DoesNotContain(Of(results, Triceps), d => d.Action == VolumeAction.Decrease);
    }

    [Fact]
    public void A_reduction_is_given_back_one_set_at_a_time_and_never_above_the_baseline()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        Run(s, ChestFatigues, 4);
        var lowered = s.Applied.Where(a => a.Muscle == nameof(Chest)).Max(a => a.MesocycleSequence);
        Assert.True(s.Applied.First(a => a.Muscle == nameof(Chest) && a.MesocycleSequence == lowered).Sets < Base(s, Chest));
        var recovered = Run(s, new Behavior(), 6);
        var chest = Of(recovered, Chest);
        Assert.Contains(chest, d => d.Action == VolumeAction.Restore);
        Assert.All(chest, d => Assert.True(d.ProposedSets <= d.BaselineSets));
        Assert.Equal(Base(s, Chest), chest.Last().ProposedSets);
    }

    [Fact]
    public void Pain_is_not_learned_from_and_prevents_giving_a_reduction_back()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        Run(s, ChestFatigues, 3);
        var painful = new Behavior { Pain = (period, index) => index == 0 && period % 2 == 0 };
        var results = Run(s, painful, 3);
        Assert.All(results.SelectMany(r => r.Decisions), d => Assert.NotEqual(VolumeAction.Restore, d.Action));
        Assert.All(results.SelectMany(r => r.Decisions), d => Assert.True(d.ProposedSets <= d.BaselineSets));
    }

    [Fact]
    public void Insufficient_data_changes_nothing()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, new Behavior { Attends = (period, _) => period % 4 == 1 }, 3);
        Assert.All(results.SelectMany(r => r.Decisions), d => Assert.Equal(d.BaselineSets, d.ProposedSets));
        Assert.All(results, r => Assert.Empty(r.LearnedTargets));
    }

    [Fact]
    public void A_deload_block_makes_no_decisions()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        Run(s, ChestFatigues, 2);
        var deload = s.RunBlock(new Behavior(), deload: true);
        Assert.Empty(deload.Result.Decisions);
    }

    [Fact]
    public void A_maintained_muscle_never_goes_above_its_maintenance_target_and_an_emphasised_one_never_under_its_normal_dose()
    {
        var profile = With(PlanTestCatalog.Profile(days: 4), (Triceps, TrainingMusclePriority.Maintain), (Chest, TrainingMusclePriority.Emphasize));
        var s = new PlanSimulator(profile);
        var results = Run(s, ChestFatigues, 6);
        Assert.All(Of(results, Triceps), d => Assert.True(d.ProposedSets <= Base(s, Triceps)));
        var chest = s.Baselines.First(b => b.Muscle == Chest);
        Assert.All(Of(results, Chest), d => Assert.InRange(d.ProposedSets, chest.Floor, chest.Target));
    }

    [Fact]
    public void Missing_feedback_is_never_read_as_a_bad_answer()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, new Behavior { Feedback = (_, _) => null }, 4);
        Assert.All(results.SelectMany(r => r.Decisions), d => Assert.Equal(d.BaselineSets, d.ProposedSets));
    }

    // ---- The decision itself, without the simulator: what each outcome says and does.

    private static TrainingMuscleBaseline Baseline(decimal target = 12m, decimal floor = 7m) => new(Quads, TrainingMusclePriority.Grow, target, floor);

    private static BlockSummary Block(BlockVerdict verdict, bool pain = false, int unfavorable = 0) =>
        new(Guid.Empty, 1, 4, 0, unfavorable, unfavorable, unfavorable, 10m, 12m, 1, 4, pain, verdict);

    [Fact]
    public void Abstaining_keeps_the_previous_target_exactly_and_says_so()
    {
        // A muscle that was lowered to 10 and now has no readable block keeps 10: it is neither restored to 12 nor lowered further.
        var decision = LongitudinalVolumePlanner.Decide(Baseline(), 10m, []);
        Assert.Equal(VolumeAction.Abstain, decision.Action);
        Assert.Equal(10m, decision.ProposedSets);
        Assert.Contains("como estaba (10", decision.Reason);
        var insufficient = LongitudinalVolumePlanner.Decide(Baseline(), 10m, [Block(BlockVerdict.Insufficient)]);
        Assert.Equal(VolumeAction.Abstain, insufficient.Action);
        Assert.Equal(10m, insufficient.ProposedSets);
    }

    [Fact]
    public void Pain_in_the_recent_blocks_holds_a_reduction_instead_of_restoring_it()
    {
        var stable = LongitudinalVolumePlanner.Decide(Baseline(), 10m, [Block(BlockVerdict.Stable)]);
        Assert.Equal(VolumeAction.Restore, stable.Action);
        Assert.Equal(11m, stable.ProposedSets);
        var withPain = LongitudinalVolumePlanner.Decide(Baseline(), 10m, [Block(BlockVerdict.Stable, pain: true)]);
        Assert.Equal(VolumeAction.Hold, withPain.Action);
        Assert.Equal(10m, withPain.ProposedSets);
        Assert.Contains("dolor", withPain.Reason);
    }

    [Fact]
    public void A_reduction_has_its_own_reason_and_stops_at_the_floor()
    {
        var down = LongitudinalVolumePlanner.Decide(Baseline(), 12m, [Block(BlockVerdict.Unfavorable, unfavorable: 2)]);
        Assert.Equal(VolumeAction.Decrease, down.Action);
        Assert.Equal(11m, down.ProposedSets);
        Assert.Contains("fatiga", down.Reason);
        var atFloor = LongitudinalVolumePlanner.Decide(Baseline(floor: 9m), 9m, [Block(BlockVerdict.Unfavorable, unfavorable: 2)]);
        Assert.Equal(VolumeAction.Hold, atFloor.Action);
        Assert.Equal(9m, atFloor.ProposedSets);
    }

    [Fact]
    public void A_stable_block_at_the_baseline_holds_and_never_raises()
    {
        var decision = LongitudinalVolumePlanner.Decide(Baseline(), 12m, [Block(BlockVerdict.Stable)]);
        Assert.Equal(VolumeAction.Hold, decision.Action);
        Assert.Equal(12m, decision.ProposedSets);
        Assert.Null(decision.LearnedTarget);
    }

    [Fact]
    public void A_baseline_that_moved_down_pulls_a_stale_previous_target_inside_the_new_band()
    {
        // The plan was reorganised and its baseline fell to 8: a previous target of 12 cannot carry over it.
        var decision = LongitudinalVolumePlanner.Decide(Baseline(target: 8m, floor: 5m), 12m, [Block(BlockVerdict.Stable)]);
        Assert.Equal(8m, decision.ProposedSets);
    }
}
