using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Direct arm work in full-body hypertrophy plans, judged against the real catalog.</summary>
public sealed class FullBodyArmWorkTests
{
    public static TheoryData<TrainingExperience, int, int> Profiles()
    {
        var data = new TheoryData<TrainingExperience, int, int>();
        foreach (var experience in new[] { TrainingExperience.Beginner, TrainingExperience.Intermediate, TrainingExperience.Advanced })
        foreach (var days in new[] { 2, 3 })
        foreach (var minutes in new[] { 60, 90 })
            data.Add(experience, days, minutes);
        return data;
    }

    private static IReadOnlyList<(TrainingExerciseCandidate Exercise, int Sets, int Minutes)> Week(
        TrainingGoal goal, TrainingExperience experience, int days, int minutes)
    {
        DayOfWeek[] weekdays = days == 2
            ? [DayOfWeek.Monday, DayOfWeek.Thursday]
            : [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
        var profile = new PlanProfile(goal, experience, new DateOnly(2026, 11, 16), 4, weekdays, minutes, [], [], [],
            TrainingProgramSplit.FullBody, false, 0, PlanSchedulingMode.Calendar, days);
        var catalog = RealCatalog.Candidates();
        var generation = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), catalog);
        Assert.True(generation.Succeeded, string.Join("; ", generation.Errors));
        var byId = catalog.ToDictionary(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase);
        return generation.Sessions.Where(s => s.WeekNumber == 3)
            .SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson).Select(e => (byId[e.ExerciseId], e.Sets, s.EstimatedDurationMinutes)))
            .ToArray();
    }

    private static bool Direct(TrainingExerciseCandidate exercise, TrainingMuscleGroup arm) =>
        exercise.MovementPattern == TrainingMovementPattern.Accessory && exercise.PrimaryMuscleGroups.Contains(arm);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void AFullBodyHypertrophyWeekWithTimeTrainsBothArmsDirectly(TrainingExperience experience, int days, int minutes)
    {
        var week = Week(TrainingGoal.Hypertrophy, experience, days, minutes);

        Assert.Contains(week, x => Direct(x.Exercise, TrainingMuscleGroup.Biceps));
        Assert.Contains(week, x => Direct(x.Exercise, TrainingMuscleGroup.Triceps));
        Assert.All(week, x => Assert.InRange(x.Minutes, 1, minutes));
    }
}
