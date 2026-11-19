using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// The limits of the longitudinal decision checked against each other, not one at a time: a condition must not cancel another. The decision
/// is run over every combination of target, floor, previous target (including stale ones outside the band), verdict and pain, and over long
/// random histories, and the same invariants must hold at every step. These are the product's own rules (they are not physiology).
/// </summary>
public sealed class LongitudinalLimitsTests
{
    private static BlockSummary Block(BlockVerdict verdict, bool pain) =>
        new(Guid.Empty, 1, verdict == BlockVerdict.Insufficient ? 1 : 4, 0, 0, verdict == BlockVerdict.Unfavorable ? 2 : 0, 0, 10m, 12m, 1, 4, pain, verdict);

    private static TrainingMuscleBaseline Baseline(decimal target, decimal floor) => new(TrainingMuscleGroup.Chest, TrainingMusclePriority.Grow, target, floor);

    /// <summary>The band the policy allows: from the floor (never under 3 sets below the target) to the target itself.</summary>
    private static (decimal Low, decimal High) Band(TrainingMuscleBaseline b) =>
        (Math.Min(b.Target, Math.Max(b.Floor, b.Target - LongitudinalPolicy.MaximumBelowBaselineSets)), b.Target);

    private static void AssertInvariants(TrainingMuscleBaseline b, decimal? previousApplied, VolumeDecision d, BlockSummary? last, bool pain)
    {
        var (low, high) = Band(b);
        var previous = Math.Clamp(previousApplied ?? b.Target, low, high);
        Assert.InRange(d.ProposedSets, low, high);                                        // never over the plan's target, never under the minimum
        Assert.True(d.ProposedSets >= b.Target - LongitudinalPolicy.MaximumBelowBaselineSets || d.ProposedSets >= b.Floor);
        Assert.True(Math.Abs(d.ProposedSets - previous) <= LongitudinalPolicy.StepSets);  // one set per block at most
        Assert.Equal(previous, d.PreviousSets);
        switch (d.Action)
        {
            case VolumeAction.Decrease:
                Assert.Equal(BlockVerdict.Unfavorable, last?.Verdict);
                Assert.True(d.ProposedSets < previous);
                break;
            case VolumeAction.Restore:
                Assert.False(pain, "nothing is given back while pain is recorded");
                Assert.NotEqual(BlockVerdict.Unfavorable, last?.Verdict);
                Assert.True(d.ProposedSets > previous && d.ProposedSets <= b.Target);
                break;
            default: // Hold and Abstain never move the target
                Assert.Equal(previous, d.ProposedSets);
                break;
        }
        if (last is null) Assert.Equal(VolumeAction.Abstain, d.Action);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    [Fact]
    public void Every_combination_of_limits_keeps_every_rule()
    {
        decimal[] targets = [4m, 8m, 12m, 16m, 18m, 20m];
        decimal[] floors = [2m, 4m, 7m, 10m, 12m];
        var verdicts = Enum.GetValues<BlockVerdict>();
        var count = 0;
        foreach (var target in targets)
            foreach (var floor in floors.Where(f => f <= target))
            {
                var b = Baseline(target, floor);
                foreach (var previous in new decimal?[] { null, target + 4, target, target - 1, target - 2, target - 3, target - 4, floor, floor - 1, 0m })
                    foreach (var verdict in verdicts)
                        foreach (var pain in new[] { false, true })
                        {
                            var blocks = new[] { Block(verdict, pain) };
                            var d = LongitudinalVolumePlanner.Decide(b, previous, blocks);
                            AssertInvariants(b, previous, d, verdict == BlockVerdict.Insufficient ? null : blocks[0], pain);
                            count++;
                        }
            }
        Assert.True(count > 1000);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Long_random_histories_never_leave_the_band_nor_move_more_than_one_set(int seed)
    {
        var random = new Random(seed);
        var b = Baseline(14m, 9m);
        var (low, high) = Band(b);
        decimal? previous = null;
        var history = new List<BlockSummary>();
        var lowestSeen = high;
        for (var step = 0; step < 300; step++)
        {
            var verdict = (BlockVerdict)random.Next(0, 3);
            history.Add(Block(verdict, pain: random.Next(0, 5) == 0));
            var d = LongitudinalVolumePlanner.Decide(b, previous, history);
            var last = history.LastOrDefault(x => x.Verdict != BlockVerdict.Insufficient);
            AssertInvariants(b, previous, d, last, history.TakeLast(2).Any(x => x.Pain));
            previous = d.ProposedSets;
            lowestSeen = Math.Min(lowestSeen, previous.Value);
        }
        Assert.True(lowestSeen >= low && lowestSeen >= b.Target - 3m);
    }

    [Fact]
    public void Several_reductions_in_a_row_stop_at_three_sets_under_the_baseline_and_come_back_one_set_at_a_time()
    {
        var b = Baseline(14m, 9m);
        var history = new List<BlockSummary>();
        decimal? previous = null;
        var path = new List<decimal> { b.Target };
        for (var i = 0; i < 6; i++)
        {
            history.Add(Block(BlockVerdict.Unfavorable, pain: false));
            previous = LongitudinalVolumePlanner.Decide(b, previous, history).ProposedSets;
            path.Add(previous.Value);
        }
        Assert.Equal([14m, 13m, 12m, 11m, 11m, 11m, 11m], path);         // 3 under the baseline, then it holds (the floor is not reached)
        for (var i = 0; i < 5; i++)
        {
            history.Add(Block(BlockVerdict.Stable, pain: false));
            // the pain flag of the reduced blocks is gone: only clean blocks follow
            previous = LongitudinalVolumePlanner.Decide(b, previous, history).ProposedSets;
            path.Add(previous.Value);
        }
        Assert.Equal([12m, 13m, 14m, 14m, 14m], path.Skip(7).ToArray());  // one set per block, never above the plan's target
    }

    [Fact]
    public void The_floor_wins_over_the_three_set_band_when_it_is_higher()
    {
        var b = Baseline(14m, 13m); // the floor allows only one set of reduction
        decimal? previous = null;
        var history = new List<BlockSummary>();
        for (var i = 0; i < 5; i++)
        {
            history.Add(Block(BlockVerdict.Unfavorable, false));
            previous = LongitudinalVolumePlanner.Decide(b, previous, history).ProposedSets;
        }
        Assert.Equal(13m, previous);
    }

    [Fact]
    public void Recent_pain_blocks_the_recovery_and_the_recovery_resumes_two_clean_blocks_later()
    {
        var b = Baseline(14m, 9m);
        var history = new List<BlockSummary> { Block(BlockVerdict.Unfavorable, false), Block(BlockVerdict.Unfavorable, false) };
        var lowered = LongitudinalVolumePlanner.Decide(b, 14m, history).ProposedSets;
        Assert.Equal(13m, lowered);
        history.Add(Block(BlockVerdict.Stable, pain: true));
        Assert.Equal(VolumeAction.Hold, LongitudinalVolumePlanner.Decide(b, lowered, history).Action);   // pain in the latest block
        history.Add(Block(BlockVerdict.Stable, pain: false));
        Assert.Equal(VolumeAction.Hold, LongitudinalVolumePlanner.Decide(b, lowered, history).Action);   // still inside the last two blocks
        history.Add(Block(BlockVerdict.Stable, pain: false));
        var restored = LongitudinalVolumePlanner.Decide(b, lowered, history);
        Assert.Equal(VolumeAction.Restore, restored.Action);
        Assert.Equal(14m, restored.ProposedSets);
    }

    [Fact]
    public void Insufficient_blocks_after_a_reduction_keep_it_exactly()
    {
        var b = Baseline(14m, 9m);
        var history = new[] { Block(BlockVerdict.Insufficient, false), Block(BlockVerdict.Insufficient, false) };
        var d = LongitudinalVolumePlanner.Decide(b, 12m, history);
        Assert.Equal(VolumeAction.Abstain, d.Action);
        Assert.Equal(12m, d.ProposedSets);
    }

    [Fact]
    public void Observations_of_another_plan_never_decide_this_plan()
    {
        var plan = Guid.NewGuid();
        var other = Guid.NewGuid();
        var b = Baseline(14m, 9m);
        var poor = Enumerable.Range(1, 4).Select(week => new Trazo.Domain.Planning.MuscleWeekObservation
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), PlanId = other, Period = week, MesocycleSequence = 1, Muscle = "Chest", Quality = "valid",
            PrescribedSets = 14, PerformedSets = 14, Exposures = 4, CompletedSets = 14, BelowRangeSets = 6, PerformanceChangePercent = -4m,
            FatigueSignals = 3, UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();
        var mine = LongitudinalEngine.Propose(plan, 2, [b], poor, []);
        Assert.Empty(mine.Decisions);                       // the first block of a plan has nothing of its own to read
        Assert.Empty(mine.LearnedTargets);
        var theirs = LongitudinalEngine.Propose(other, 2, [b], poor, []);
        Assert.Single(theirs.Decisions);                    // positive control: the same rows do decide for the plan they belong to
        Assert.Equal(VolumeAction.Decrease, theirs.Decisions[0].Action);
    }

    [Fact]
    public void Decisions_never_read_the_block_they_are_deciding_nor_later_ones()
    {
        var plan = Guid.NewGuid();
        var b = Baseline(14m, 9m);
        // Block 2 is the one being generated: its own (poor) rows must not decide its own dose.
        var rows = Enumerable.Range(5, 4).Select(week => new Trazo.Domain.Planning.MuscleWeekObservation
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), PlanId = plan, Period = week, MesocycleSequence = 2, Muscle = "Chest", Quality = "valid",
            PrescribedSets = 14, PerformedSets = 14, Exposures = 4, CompletedSets = 14, BelowRangeSets = 6, PerformanceChangePercent = -4m,
            FatigueSignals = 3, UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();
        Assert.Empty(LongitudinalEngine.Propose(plan, 2, [b], rows, []).Decisions);
    }
}
