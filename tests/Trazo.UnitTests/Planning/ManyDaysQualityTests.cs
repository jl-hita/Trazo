using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Five and six days over the real catalog: no filler (the weekly ceiling holds), no exercise repeated inside a session or
/// piled on one week, and a new block renews part of the accessories without dropping everything.
/// </summary>
public sealed class ManyDaysQualityTests(ITestOutputHelper output)
{
    private static readonly string[] Gym = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    public static IEnumerable<object[]> Layouts() =>
        from layout in new (TrainingProgramSplit Split, int Days)[]
            { (TrainingProgramSplit.PushPullLegs, 5), (TrainingProgramSplit.PushPullLegs, 6), (TrainingProgramSplit.Hybrid, 5) }
        from experience in new[] { TrainingExperience.Intermediate, TrainingExperience.Advanced }
        from minutes in new[] { 60, 90 }
        select new object[] { layout.Split, layout.Days, experience, minutes };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void FiveAndSixDaysStayInsideTheirLimitsAndRenewBetweenBlocks(
        TrainingProgramSplit split, int days, TrainingExperience experience, int minutes)
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, experience, days, Gym, null, null, 12, minutes, split);
        var catalog = RealCatalog.Candidates();
        var accessories = catalog.Where(c => c.MovementPattern == TrainingMovementPattern.Accessory).Select(c => c.ExerciseId).ToHashSet();
        var first = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), catalog);
        Assert.True(first.Succeeded, string.Join(" | ", first.Errors));
        var firstIds = first.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId).Distinct().ToList();
        var second = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(2, 1, "accumulation", 5, 4), catalog,
            previousBlockExerciseIds: firstIds, continuesPreviousBlock: true);
        Assert.True(second.Succeeded, string.Join(" | ", second.Errors));

        foreach (var generation in new[] { first, second })
            foreach (var week in generation.Sessions.GroupBy(s => s.WeekNumber))
            {
                var sessions = week.Select(s => PrescriptionJson.Read(s.PrescriptionJson)).ToList();
                Assert.All(sessions, s => Assert.Equal(s.Count, s.Select(e => e.ExerciseId).Distinct().Count()));
                var uses = sessions.SelectMany(s => s).GroupBy(e => e.ExerciseId).ToList();
                Assert.All(uses, g => Assert.True(g.Count() <= 2, $"{g.Key} {g.Count()} times in a week"));
                var perMuscle = sessions.SelectMany(s => s).SelectMany(e => e.StimulatedMuscles().Select(m => (m, e.Sets)))
                    .GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Sum(x => x.Sets));
                Assert.All(perMuscle, p => Assert.True(p.Value <= 22, $"{p.Key} {p.Value}"));
            }

        var before = firstIds.Where(accessories.Contains).ToHashSet();
        var after = second.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Select(e => e.ExerciseId)
            .Where(accessories.Contains).Distinct().ToList();
        var renewed = after.Count(id => !before.Contains(id));
        var used = first.Sessions.Where(s => s.WeekNumber == 3).Average(s => s.EstimatedDurationMinutes);
        output.WriteLine($"{split} {days}d {experience} {minutes}min: accessories {after.Count}, new {renewed}; average session {used:0} min");
        Assert.True(renewed > 0, "the second block repeats every accessory");
        Assert.True(renewed < after.Count || after.Count <= 2, "the second block drops every accessory");
    }
}
