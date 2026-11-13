using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingStrengthProfileTests
{
    [Theory]
    [InlineData(TrainingExperience.Intermediate)]
    [InlineData(TrainingExperience.Advanced)]
    public void StrengthPrimaryLiftUsesLowRepsAndLongRest(TrainingExperience experience)
    {
        var plan = TrainingBlockProgressionTests.Generate(2, TrainingGoal.Strength, 3, 120, experience);
        foreach (var session in plan.Sessions)
        {
            var primary = session.Exercises[0];
            Assert.Equal((3, 6, 240), (primary.MinRepetitions, primary.MaxRepetitions, primary.RestSeconds));
            Assert.All(session.Exercises.Skip(1), exercise =>
                Assert.NotEqual((3, 6, 240), (exercise.MinRepetitions, exercise.MaxRepetitions, exercise.RestSeconds)));
        }
    }

    [Fact]
    public void StrengthSecondaryCompoundsKeepTheirRangeAndRest()
    {
        var plan = TrainingBlockProgressionTests.Generate(2, TrainingGoal.Strength, 3, 120);
        var second = plan.Sessions[0].Exercises[1];
        Assert.Equal((5, 8, 180), (second.MinRepetitions, second.MaxRepetitions, second.RestSeconds));
    }

    [Fact]
    public void BeginnersKeepTheFiveToEightRangeToConsolidateTechnique()
    {
        var plan = TrainingBlockProgressionTests.Generate(2, TrainingGoal.Strength, 3, 120, TrainingExperience.Beginner);
        var primary = plan.Sessions[0].Exercises[0];
        Assert.Equal((5, 8, 180), (primary.MinRepetitions, primary.MaxRepetitions, primary.RestSeconds));
    }

    [Theory]
    [InlineData(TrainingGoal.Hypertrophy)]
    [InlineData(TrainingGoal.GeneralFitness)]
    public void OtherGoalsAreNotAffected(TrainingGoal goal)
    {
        var plan = TrainingBlockProgressionTests.Generate(2, goal, 3, 120);
        Assert.All(plan.Sessions.SelectMany(s => s.Exercises), e => Assert.NotEqual(240, e.RestSeconds));
    }

    [Fact]
    public void StrengthSessionsStayWithinTheRequestedDuration()
    {
        var plan = TrainingBlockProgressionTests.Generate(2, TrainingGoal.Strength, 3, 60, TrainingExperience.Advanced);
        Assert.All(plan.Sessions, s => Assert.InRange(s.EstimatedDurationMinutes, 1, 60));
    }

    [Fact]
    public void FiveDayPushPullLegsHypertrophyDoesNotFlagSingleWeeklyLegExposureAsAFrequencyDefect()
    {
        var plan = TrainingBlockProgressionTests.Generate(
            1, TrainingGoal.Hypertrophy, 5, 90, TrainingExperience.Intermediate, TrainingProgramSplit.PushPullLegs);

        Assert.DoesNotContain(plan.QualityReport!.Warnings, warning =>
            warning.Contains("Quadriceps recibe estímulo efectivo en 1 sesión", StringComparison.Ordinal));
    }
}
