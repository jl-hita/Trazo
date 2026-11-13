using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingMacrocycleDraftTests
{
    private static readonly DateOnly Start = new(2026, 11, 9);

    private static TrainingMacrocycleDraft Build(TrainingMacrocycleDraftRequest request)
    {
        var result = TrainingMacrocycleDraftBuilder.Build(request);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Draft!;
    }

    [Fact]
    public void DefaultDraftNeverContainsDeloadPhases()
    {
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var weeks in new[] { 4, 8, 12, 24, 52 })
            Assert.DoesNotContain(Build(new(Start, weeks, goal, TrainingExperience.Advanced)).Phases,
                phase => phase.PhaseType == "deload");
    }

    [Fact]
    public void StrengthDraftSplitsAccumulationAndIntensificationAndCoversEveryWeek()
    {
        var draft = Build(new(Start, 8, TrainingGoal.Strength, TrainingExperience.Intermediate));
        Assert.Equal(["accumulation", "intensification"], draft.Phases.Select(p => p.PhaseType));
        AssertContiguous(draft, 8);
    }

    [Fact]
    public void BeginnerAndHypertrophyStayInAccumulation()
    {
        Assert.All(Build(new(Start, 12, TrainingGoal.Strength, TrainingExperience.Beginner)).Phases,
            p => Assert.Equal("accumulation", p.PhaseType));
        Assert.All(Build(new(Start, 12, TrainingGoal.Hypertrophy, TrainingExperience.Advanced)).Phases,
            p => Assert.Equal("accumulation", p.PhaseType));
    }

    [Fact]
    public void DeloadsAppearOnlyWhenRequestedAtTheChosenIntervalAndNeverLast()
    {
        var draft = Build(new(Start, 11, TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, true, 4));
        var deloads = draft.Phases.Where(p => p.PhaseType == "deload").ToArray();
        Assert.Equal(2, deloads.Length);
        Assert.All(deloads, d => Assert.Equal(7, d.EndDate.DayNumber - d.StartDate.DayNumber + 1));
        Assert.Equal(Start.AddDays(4 * 7), deloads[0].StartDate);
        Assert.NotEqual("deload", draft.Phases[^1].PhaseType);
        AssertContiguous(draft, 11);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(53)]
    public void InvalidDurationIsRejected(int weeks)
    {
        Assert.False(TrainingMacrocycleDraftBuilder.Build(
            new(Start, weeks, TrainingGoal.Strength, TrainingExperience.Beginner)).Succeeded);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void DeloadIntervalOutsideTheEditableReferenceRangeIsRejectedOnlyWhenDeloadsAreRequested(int interval)
    {
        Assert.False(TrainingMacrocycleDraftBuilder.Build(
            new(Start, 12, TrainingGoal.Strength, TrainingExperience.Beginner, true, interval)).Succeeded);
        Assert.True(TrainingMacrocycleDraftBuilder.Build(
            new(Start, 12, TrainingGoal.Strength, TrainingExperience.Beginner, false, interval)).Succeeded);
    }

    [Fact]
    public void NotesStateThatTheSchemeIsAnInternalHeuristicNotAcsm()
    {
        var notes = Build(new(Start, 8, TrainingGoal.Strength, TrainingExperience.Advanced)).Notes;
        Assert.Contains("heurística interna", notes, StringComparison.Ordinal);
        Assert.Contains("no es un esquema prescrito por ACSM", notes, StringComparison.Ordinal);
    }

    private static void AssertContiguous(TrainingMacrocycleDraft draft, int weeks)
    {
        Assert.Equal(Start, draft.Phases[0].StartDate);
        for (var i = 1; i < draft.Phases.Count; i++)
            Assert.Equal(draft.Phases[i - 1].EndDate.AddDays(1), draft.Phases[i].StartDate);
        Assert.Equal(Start.AddDays(weeks * 7 - 1), draft.Phases[^1].EndDate);
        Assert.Equal(draft.EndDate, draft.Phases[^1].EndDate);
    }
}
