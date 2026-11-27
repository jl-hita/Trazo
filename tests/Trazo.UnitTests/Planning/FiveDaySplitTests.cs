using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The five-day layouts over the real catalog: the calendar hybrid (upper, lower, push, legs, pull) and the upper/lower
/// rotation at five sessions a week. Volume, frequency, time and recovery.
/// </summary>
public sealed class FiveDaySplitTests(ITestOutputHelper output)
{
    private static readonly string[] Gym = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];
    private static readonly TrainingMuscleGroup[] Major =
    [
        TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Quadriceps,
        TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Glutes
    ];

    private static PlanProfile Hybrid(TrainingExperience experience, int minutes) =>
        PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, experience, 5, Gym, null, null, 12, minutes, TrainingProgramSplit.Hybrid);

    private static IEnumerable<TrainingExercisePrescription> Exercises(GeneratedSession session) =>
        PrescriptionJson.Read(session.PrescriptionJson);

    [Theory]
    [InlineData(TrainingExperience.Intermediate, 60)]
    [InlineData(TrainingExperience.Intermediate, 90)]
    [InlineData(TrainingExperience.Advanced, 60)]
    [InlineData(TrainingExperience.Advanced, 90)]
    public void TheHybridTrainsEveryMajorMuscleTwiceAWeek_InsideItsLimits(TrainingExperience experience, int minutes)
    {
        var generation = MesocycleGenerator.Generate(Hybrid(experience, minutes), 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());
        Assert.True(generation.Succeeded, string.Join(" | ", generation.Errors));

        foreach (var week in generation.Sessions.GroupBy(s => s.WeekNumber))
        {
            var sessions = week.ToList();
            Assert.Equal(["Tren superior A", "Tren inferior A", "Empuje", "Pierna", "Tracción"], sessions.Select(s => s.Title));
            Assert.All(sessions, s => Assert.True(s.EstimatedDurationMinutes <= minutes, $"{s.Title}: {s.EstimatedDurationMinutes}"));
            foreach (var muscle in Major)
            {
                var exposures = sessions.Count(s => Exercises(s).Any(e => e.StimulatedMuscles().Contains(muscle)));
                var sets = sessions.SelectMany(Exercises).Where(e => e.StimulatedMuscles().Contains(muscle)).Sum(e => e.Sets);
                if (week.Key == 3) output.WriteLine($"{experience} {minutes} min: {muscle} {sets} sets in {exposures} sessions");
                Assert.True(exposures >= 2, $"week {week.Key}: {muscle} only {exposures}");
                // Never past the absolute weekly ceiling, plus the set the peak week may add.
                Assert.True(sets <= 21, $"week {week.Key}: {muscle} {sets}");
            }
            // Recovery: on consecutive days no muscle is a primary mover twice.
            foreach (var (first, second) in sessions.Zip(sessions.Skip(1)))
            {
                if (second.Date!.Value.DayNumber - first.Date!.Value.DayNumber > 1) continue;
                var repeated = Exercises(first).SelectMany(e => e.PrimaryMuscleGroups)
                    .Intersect(Exercises(second).SelectMany(e => e.PrimaryMuscleGroups))
                    .Where(m => m is not (TrainingMuscleGroup.Abdominals or TrainingMuscleGroup.Obliques)).ToList();
                Assert.True(repeated.Count == 0, $"week {week.Key} {first.Title} -> {second.Title}: {string.Join(", ", repeated)}");
            }
        }
    }

    [Fact]
    public void TheHybridNeedsFiveDays_AndRotatesInASequencePlan()
    {
        var four = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Advanced, 4, Gym, null, null, 12, 60, TrainingProgramSplit.Hybrid);
        Assert.False(MesocycleGenerator.Generate(four, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates()).Succeeded);
        Assert.True(TrainingSessionRotation.IsSupportedSequence(TrainingProgramSplit.Hybrid, 5));
        Assert.False(TrainingSessionRotation.IsSupportedSequence(TrainingProgramSplit.Hybrid, 3));

        var rotation = Hybrid(TrainingExperience.Advanced, 60) with { SchedulingMode = PlanSchedulingMode.Sequence, SessionsPerPeriod = 4, TrainingDays = [] };
        var generation = MesocycleGenerator.Generate(rotation, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());
        Assert.True(generation.Succeeded, string.Join(" | ", generation.Errors));
        Assert.Equal(["Tren superior A", "Tren inferior A", "Empuje", "Pierna", "Tracción", "Tren superior A"],
            generation.Sessions.Take(6).Select(s => s.Title));
    }

    [Fact]
    public void UpperLowerAtFiveSessionsAWeekAlternatesAndKeepsItsTime()
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Advanced, 4, Gym, null, null, 12, 75, TrainingProgramSplit.UpperLower)
            with { SchedulingMode = PlanSchedulingMode.Sequence, SessionsPerPeriod = 5, TrainingDays = [] };
        var generation = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());
        Assert.True(generation.Succeeded, string.Join(" | ", generation.Errors));
        Assert.Equal(20, generation.Sessions.Count);
        // Upper and lower always alternate, across the periods too.
        Assert.All(generation.Sessions.Zip(generation.Sessions.Skip(1)),
            pair => Assert.NotEqual(pair.First.Title.Contains("superior"), pair.Second.Title.Contains("superior")));
        Assert.All(generation.Sessions, s => Assert.True(s.EstimatedDurationMinutes <= 75));
    }
}
