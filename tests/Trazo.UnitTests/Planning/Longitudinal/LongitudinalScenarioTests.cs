using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// End-to-end scenarios of the longitudinal layer on the real generator, observer and engine (no database). Each one answers the
/// acceptance question: does history move the next block's dose prudently, and does it leave it alone when the data does not justify it?
/// </summary>
public sealed class LongitudinalScenarioTests
{
    private const TrainingMuscleGroup Chest = TrainingMuscleGroup.Chest;
    private const TrainingMuscleGroup Triceps = TrainingMuscleGroup.Triceps;
    private const TrainingMuscleGroup Quads = TrainingMuscleGroup.Quadriceps;

    private static PlanProfile With(PlanProfile p, params (TrainingMuscleGroup, TrainingMusclePriority)[] priorities) =>
        p with { MusclePriorities = priorities.ToDictionary(x => x.Item1, x => x.Item2) };

    private static decimal Dose(LongitudinalResult r, PlanSimulator s, TrainingMuscleGroup m) =>
        r.LearnedTargets.TryGetValue(m, out var v) ? v : s.Baselines.First(b => b.Muscle == m).Target;

    private static decimal Base(PlanSimulator s, TrainingMuscleGroup m) => s.Baselines.First(b => b.Muscle == m).Target;

    private static List<LongitudinalResult> Run(PlanSimulator s, Behavior b, int blocks)
    {
        var results = new List<LongitudinalResult>();
        for (var i = 0; i < blocks; i++) results.Add(s.RunBlock(b).Result);
        return results;
    }

    private static VolumeDecision? DecisionFor(LongitudinalResult r, TrainingMuscleGroup m) => r.Decisions.FirstOrDefault(d => d.Muscle == m.ToString());

    private static List<VolumeDecision> Of(IEnumerable<LongitudinalResult> results, TrainingMuscleGroup m) =>
        results.Select(r => DecisionFor(r, m)).OfType<VolumeDecision>().ToList();

    private static int Increases(IEnumerable<LongitudinalResult> results) =>
        results.SelectMany(r => r.Decisions).Count(d => d.Action == VolumeAction.Increase);

    // 1-3: the engine works for each level and never leaves the policy limits.
    [Theory]
    [InlineData(TrainingExperience.Beginner, 3)]
    [InlineData(TrainingExperience.Intermediate, 4)]
    [InlineData(TrainingExperience.Advanced, 4)]
    public void Regular_users_of_every_level_stay_inside_the_policy_limits(TrainingExperience level, int days)
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(experience: level, days: days));
        var results = Run(s, new Behavior(), 4);
        foreach (var d in results.SelectMany(r => r.Decisions))
        {
            var b = s.Baselines.First(x => x.Muscle.ToString() == d.Muscle);
            Assert.InRange(d.ProposedSets, b.Floor, b.Ceiling);
            Assert.InRange(d.ProposedSets, d.BaselineSets - LongitudinalPolicy.MaximumBelowBaselineSets, d.BaselineSets + LongitudinalPolicy.MaximumAboveBaselineSets);
            Assert.True(Math.Abs(d.ProposedSets - d.PreviousSets) <= LongitudinalPolicy.StepSets);
        }
    }

    // 4: emphasized chest keeps its priority over the learned dose of the other muscles.
    [Fact]
    public void Emphasized_chest_stays_at_least_as_high_as_a_plain_muscle()
    {
        var s = new PlanSimulator(With(PlanTestCatalog.Profile(days: 4), (Chest, TrainingMusclePriority.Emphasize)));
        Run(s, new Behavior(), 3);
        var next = s.Decide();
        Assert.True(Dose(next, s, Chest) >= Base(s, Chest) - LongitudinalPolicy.MaximumBelowBaselineSets);
        Assert.Equal(TrainingMusclePriority.Emphasize, s.Baselines.First(b => b.Muscle == Chest).Priority);
    }

    // 5: maintenance never grows however well it goes.
    [Fact]
    public void Maintained_triceps_never_increase()
    {
        var s = new PlanSimulator(With(PlanTestCatalog.Profile(days: 4), (Triceps, TrainingMusclePriority.Maintain)));
        var results = Run(s, new Behavior(), 4);
        Assert.All(Of(results, Triceps), d => Assert.NotEqual(VolumeAction.Increase, d.Action));
        Assert.True(Dose(s.Decide(), s, Triceps) <= Base(s, Triceps));
    }

    // 6: several emphasized muscles share exercises; the learned doses stay within each muscle's own limits.
    [Fact]
    public void Several_emphasized_muscles_respect_their_own_limits()
    {
        var s = new PlanSimulator(With(PlanTestCatalog.Profile(days: 4),
            (Chest, TrainingMusclePriority.Emphasize), (Triceps, TrainingMusclePriority.Emphasize), (TrainingMuscleGroup.Deltoids, TrainingMusclePriority.Emphasize)));
        var results = Run(s, new Behavior(), 4);
        foreach (var d in results.SelectMany(r => r.Decisions))
        {
            var b = s.Baselines.First(x => x.Muscle.ToString() == d.Muscle);
            Assert.InRange(d.ProposedSets, b.Floor, b.Ceiling);
        }
    }

    // 7: good response at moderate volume lets the dose rise, one set at a time.
    [Fact]
    public void Good_response_raises_the_dose_gradually()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, new Behavior(), 5);
        var chest = Of(results, Chest);
        Assert.Contains(chest, d => d.Action == VolumeAction.Increase);
        Assert.True(chest.Max(d => d.ProposedSets) <= Base(s, Chest) + LongitudinalPolicy.MaximumAboveBaselineSets);
        Assert.All(chest, d => Assert.True(Math.Abs(d.ProposedSets - d.PreviousSets) <= LongitudinalPolicy.StepSets));
    }

    // 8: repeated fatigue at high volume lowers the dose.
    [Fact]
    public void Repeated_fatigue_lowers_the_dose()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var behavior = new Behavior { MuscleResponse = (_, m, _) => m == Quads ? Response.Poor : Response.Good };
        var results = Run(s, behavior, 5);
        var quads = Of(results, Quads);
        Assert.Contains(quads, d => d.Action == VolumeAction.Decrease);
        Assert.All(quads, d => Assert.True(d.ProposedSets <= Base(s, Quads)));
        Assert.All(quads, d => Assert.False(string.IsNullOrWhiteSpace(d.Reason)));
    }

    // 9: negative feedback on one muscle stops its increase without touching the others.
    [Fact]
    public void Negative_muscle_feedback_stops_that_muscle_only()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var behavior = new Behavior { Feedback = (_, m) => m == Chest ? ("low", "strong", "loaded") : null };
        var results = Run(s, behavior, 5);
        Assert.All(Of(results, Chest), d => Assert.NotEqual(VolumeAction.Increase, d.Action));
        Assert.Contains(results.SelectMany(r => r.Decisions).Where(d => d.Muscle != Chest.ToString()), d => d.Action == VolumeAction.Increase);
    }

    // 10: not answering is not a negative answer: the same behaviour with and without answers (all good) must not lower the dose.
    [Fact]
    public void No_feedback_never_counts_as_bad_feedback()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var results = Run(s, new Behavior(), 5);
        Assert.All(results.SelectMany(r => r.Decisions), d => Assert.NotEqual(VolumeAction.Decrease, d.Action));
    }

    // 11: pain is never learned from and blocks any increase.
    [Fact]
    public void Pain_blocks_increases_and_is_not_learned_from()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var behavior = new Behavior { Pain = (period, _) => period is >= 3 and <= 4 };
        var results = Run(s, behavior, 4);
        // The block right after the pain: no increase anywhere. Later blocks may recover once the pain is out of the window.
        Assert.All(results[1].Decisions, d => Assert.NotEqual(VolumeAction.Increase, d.Action));
    }

    // 12: occasional skips do not move the dose.
    [Fact]
    public void Occasional_skips_do_not_change_the_dose()
    {
        var regular = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var skipper = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var skips = new Behavior { Attends = (period, index) => !(index == 1 && period % 4 == 3) };
        Run(regular, new Behavior(), 3);
        Run(skipper, skips, 3);
        var a = regular.Decide();
        var b = skipper.Decide();
        Assert.DoesNotContain(b.Decisions, d => d.Action == VolumeAction.Decrease);
        Assert.True(b.Decisions.Sum(d => d.ProposedSets) <= a.Decisions.Sum(d => d.ProposedSets));
    }

    // 13: a persistent drop from 4 to 3 real days is data, a one-off is not (reorganisation lives in the frequency layer).
    [Fact]
    public void A_persistent_drop_in_attendance_does_not_raise_the_dose()
    {
        var regular = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var fewer = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var regularResults = Run(regular, new Behavior(), 4);
        var fewerResults = Run(fewer, new Behavior { Attends = (period, index) => index != period % 4 }, 4);
        Assert.True(Increases(fewerResults) < Increases(regularResults));
    }

    // 14: a deload block is skipped and does not by itself change the dose.
    [Fact]
    public void A_deload_block_makes_no_decisions()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        Run(s, new Behavior(), 2);
        var deload = s.RunBlock(new Behavior(), deload: true);
        Assert.Empty(deload.Result.Decisions);
        var after = s.Decide();
        Assert.All(after.Decisions, d => Assert.InRange(d.ProposedSets, d.BaselineSets - LongitudinalPolicy.MaximumBelowBaselineSets, d.BaselineSets + LongitudinalPolicy.MaximumAboveBaselineSets));
    }

    // 15: substituting an exercise starts a new performance history instead of reading a fake drop.
    [Fact]
    public void Exercise_substitution_is_not_read_as_a_decline()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4), seed: 7);
        var other = new PlanSimulator(PlanTestCatalog.Profile(days: 4), seed: 11);
        Run(s, new Behavior(), 3);
        Run(other, new Behavior(), 3);
        Assert.All(other.Decide().Decisions, d => Assert.NotEqual(VolumeAction.Decrease, d.Action));
    }

    // Acceptance: insufficient data leaves the block exactly as the plain engine would generate it.
    [Fact]
    public void Insufficient_data_changes_nothing()
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        var first = s.Decide();
        Assert.Empty(first.LearnedTargets);
        Assert.All(first.Decisions, d => Assert.Equal(VolumeAction.Abstain, d.Action));
        var plain = s.Generate(false, null);
        var learned = s.Generate(false, first.LearnedTargets);
        Assert.Equal(plain.Sessions.Select(x => x.PrescriptionJson), learned.Sessions.Select(x => x.PrescriptionJson));
    }

    // Acceptance: two users with the same level and priorities but different histories get different volumes, with reasons.
    [Fact]
    public void Two_similar_users_with_different_history_get_different_volumes()
    {
        var profile = PlanTestCatalog.Profile(days: 4);
        var thriving = new PlanSimulator(profile);
        var struggling = new PlanSimulator(profile);
        Run(thriving, new Behavior(), 4);
        Run(struggling, new Behavior { MuscleResponse = (_, _, _) => Response.Poor }, 4);
        var a = thriving.Decide();
        var b = struggling.Decide();
        Assert.True(a.Decisions.Sum(d => d.ProposedSets) > b.Decisions.Sum(d => d.ProposedSets));
        Assert.All(a.Decisions.Concat(b.Decisions), d => Assert.False(string.IsNullOrWhiteSpace(d.Reason)));
        Assert.Equal("longitudinal-1.0", LongitudinalPolicy.Version);
    }
}
