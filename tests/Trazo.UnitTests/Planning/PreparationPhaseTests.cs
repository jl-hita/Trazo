using Trazo.Application.Localization;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class PreparationPhaseTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static PlanProfileInput Input(string? readiness, int? weeks = null, int? total = 12) => new(
        "Hypertrophy", "Beginner", null, total, ["Monday", "Wednesday", "Friday"], null, ["dumbbell"], null, null, null, null, null,
        Readiness: readiness, PreparationWeeks: weeks);

    private static PlanProfile Profile(TrainingReadiness readiness, int preparation, int total = 12) =>
        PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, TrainingExperience.Beginner, 3, ["dumbbell", "barbell", "bench", "squat-rack", "cable", "machine"],
            weeks: total) with { Readiness = readiness, PreparationWeeks = preparation };

    [Fact]
    public void ARegularPlanHasNoPreparation_AndNothingChangesWhenTheFieldIsAbsent()
    {
        var absent = PlanProfileParser.Parse(Input(null), Today).Profile!;
        var regular = PlanProfileParser.Parse(Input("Regular"), Today).Profile!;
        Assert.Equal(0, absent.PreparationWeeks);
        Assert.Equal(TrainingReadiness.Regular, regular.Readiness);
        Assert.Equal(0, regular.PreparationWeeks);
    }

    [Theory]
    [InlineData("New", null, 2)]
    [InlineData("Returning", null, 2)]
    [InlineData("Returning", 3, 3)]
    [InlineData("New", 1, 1)]
    [InlineData("New", 4, 4)]
    public void NewAndReturningUsersGetTheirDefaultOrTheWeeksTheyChose(string readiness, int? weeks, int expected)
    {
        var result = PlanProfileParser.Parse(Input(readiness, weeks), Today);
        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Profile!.PreparationWeeks);
    }

    [Theory]
    [InlineData("Beginner-ish", null, 12, "readiness")]
    [InlineData("New", 0, 12, "preparationWeeks")]
    [InlineData("New", 5, 12, "preparationWeeks")]
    [InlineData("New", 3, 6, "preparationWeeks")]   // 6 - 3 leaves 3 weeks of plan, fewer than the minimum
    [InlineData("Regular", 2, 12, "preparationWeeks")]
    public void InvalidPreparationIsRejectedWithAMessageForTheField(string readiness, int? weeks, int total, string field)
    {
        var result = PlanProfileParser.Parse(Input(readiness, weeks, total), Today);
        Assert.False(result.Succeeded);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void ThePreparationOpensTheMacrocycle_AndThePlanProperStartsAfterIt()
    {
        var draft = MacrocyclePlanner.Build(Profile(TrainingReadiness.New, 2)).Draft!;

        var first = draft.Phases[0];
        Assert.Equal(PreparationPhase.PhaseType, first.PhaseType);
        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(Today, first.StartDate);
        Assert.Equal(Today.AddDays(13), first.EndDate);
        Assert.Equal(Today.AddDays(14), draft.Phases[1].StartDate);
        Assert.Equal(Today.AddDays(12 * 7 - 1), draft.Phases[^1].EndDate);
        Assert.Equal(Today.AddDays(12 * 7 - 1), draft.EndDate);
        Assert.Equal(Enumerable.Range(1, draft.Phases.Count), draft.Phases.Select(p => p.SequenceNumber));
        Assert.DoesNotContain(draft.Phases.Skip(1), p => p.PhaseType == PreparationPhase.PhaseType);
    }

    [Fact]
    public void DeloadsAreLaidOverThePlanProper_NeverOverThePreparation()
    {
        var withPreparation = MacrocyclePlanner.Build(Profile(TrainingReadiness.Returning, 3, total: 14)).Draft!;
        var without = MacrocyclePlanner.Build(Profile(TrainingReadiness.Regular, 0, total: 11)).Draft!;

        Assert.NotEqual("deload", withPreparation.Phases[0].PhaseType);
        // The plan proper is the same as a plan of the remaining length that starts when the preparation ends.
        Assert.Equal(without.Phases.Select(p => p.PhaseType), withPreparation.Phases.Skip(1).Select(p => p.PhaseType));
    }

    [Fact]
    public void ThePreparationRampsTheVolumeFromHalfToThreeQuartersAndKeepsTheEffortFarFromFailure()
    {
        Assert.Equal(0.5m, PreparationPhase.VolumeFactor(1, 1));
        Assert.Equal(0.5m, PreparationPhase.VolumeFactor(1, 2));
        Assert.Equal(0.75m, PreparationPhase.VolumeFactor(2, 2));
        Assert.Equal(0.625m, PreparationPhase.VolumeFactor(2, 3));
        Assert.Equal(4m, PreparationPhase.RepetitionsInReserve(1, 3));
        Assert.Equal(4m, PreparationPhase.RepetitionsInReserve(2, 3));
        Assert.Equal(3m, PreparationPhase.RepetitionsInReserve(3, 3));
        Assert.Equal(4m, PreparationPhase.RepetitionsInReserve(1, 1));
    }

    [Fact]
    public void AGeneratedPreparationBlockHasFewerSetsAndMoreRepsInReserveThanTheSameBlockAsAccumulation()
    {
        var profile = Profile(TrainingReadiness.New, 2);
        var catalog = PlanTestCatalog.Candidates();
        var normal = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 2), catalog);
        var preparation = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, PreparationPhase.PhaseType, 1, 2), catalog);
        Assert.True(normal.Succeeded && preparation.Succeeded);

        int Sets(MesocycleGeneration g, int week) => g.Sessions.Where(s => s.WeekNumber == week)
            .SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Sum(e => e.Sets);
        Assert.True(Sets(preparation, 1) < Sets(normal, 1));
        Assert.True(Sets(preparation, 2) < Sets(normal, 2));
        Assert.True(Sets(preparation, 1) <= Sets(preparation, 2));
        Assert.All(preparation.Sessions.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)), e =>
        {
            Assert.True(e.Sets >= 1);
            Assert.True(e.TargetRepetitionsInReserve >= 3m);
        });
    }

    [Fact]
    public void ApplyNeverMakesAnExerciseEasierToTheCostOfLessRepsInReserve()
    {
        var exercise = new TrainingExercisePrescription("x", "1", "X", 1, 4, 8, 12, 5m, 90, TrainingProgressionRule.DoubleProgression, "t",
            [TrainingMuscleGroup.Chest], []);
        var result = PreparationPhase.Apply([exercise], 1, 2).Single();
        Assert.Equal(2, result.Sets);
        Assert.Equal(5m, result.TargetRepetitionsInReserve);   // already further from failure than the preparation asks
        Assert.Equal(1, PreparationPhase.Apply([exercise with { Sets = 1 }], 1, 2).Single().Sets);
    }

    [Fact]
    public void EveryTextOfThePreparationExistsInEnglish()
    {
        var draft = MacrocyclePlanner.Build(Profile(TrainingReadiness.New, 2)).Draft!;
        NewTextsInBothLanguagesTests.AssertEnglish(draft.Phases[0].Name, "phase name");
        NewTextsInBothLanguagesTests.AssertEnglish(draft.Phases[0].Objective, "objective");
        NewTextsInBothLanguagesTests.AssertEnglish(draft.Notes, "notes");
        foreach (var message in PlanProfileParser.Parse(Input("Nope", 9, 5), Today).Errors.Values.SelectMany(m => m))
            NewTextsInBothLanguagesTests.AssertEnglish(message, message);
        Assert.Equal("Preparation", Localizer.Translate("Preparación", AppLanguage.En));
    }
}
