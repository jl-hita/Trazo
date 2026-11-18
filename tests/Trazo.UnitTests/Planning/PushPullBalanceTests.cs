using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Judged against the real exercise library, as a coach would: the chest must not be left at the bottom of its range while the back is
/// above it. The lats used to have a direct accessory reserved and the chest did not, so upper sessions ended with about 10 sets of
/// chest against 16 of lats and 13 of upper back.
/// </summary>
public sealed class PushPullBalanceTests
{
    public static IEnumerable<object[]> Layouts()
    {
        foreach (var experience in new[] { TrainingExperience.Intermediate, TrainingExperience.Advanced })
        foreach (var (split, days) in new[]
                 {
                     (TrainingProgramSplit.UpperLower, 4), (TrainingProgramSplit.FullBody, 3), (TrainingProgramSplit.FullBody, 2),
                     (TrainingProgramSplit.PushPullLegs, 3), (TrainingProgramSplit.PushPullLegs, 6)
                 })
            yield return [experience, split, days];
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheChestIsNotLeftBehindTheBack(TrainingExperience experience, TrainingProgramSplit split, int days)
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, experience, days, [], split: split, minutes: 75);
        var block = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));

        var week = block.Quality!.WeeklyMuscleVolumes[2].Muscles.ToDictionary(m => m.MuscleGroup, m => m.EstimatedEffectiveSets);
        Assert.True(week[TrainingMuscleGroup.Chest] >= 10m, $"chest {week[TrainingMuscleGroup.Chest]} sets");
        Assert.True(week[TrainingMuscleGroup.Chest] >= 0.65m * week[TrainingMuscleGroup.Lats],
            $"chest {week[TrainingMuscleGroup.Chest]} against lats {week[TrainingMuscleGroup.Lats]}");
    }
}
