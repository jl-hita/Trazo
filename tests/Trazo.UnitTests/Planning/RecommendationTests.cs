using Trazo.Application.Localization;
using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class RecommendationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Next = Guid.NewGuid();

    private static IReadOnlyList<RecommendationView> Build(IEnumerable<ExerciseExposure> exposures, RecentAdherence? adherence = null, bool pending = true) =>
        Recommendations.Build(exposures.ToList(), adherence, t => t, id => id, _ => pending ? Next : null);

    private static IEnumerable<ExerciseExposure> Series(string id, params decimal[] estimates) =>
        estimates.Select((e, i) => new ExerciseExposure(id, Start.AddDays(i * 7), e, false, true));

    [Fact]
    public void AStalledExerciseNeedsEnoughHistory_AndOneStillInThePlan()
    {
        var flat = Series("bench", 100, 102, 104, 104, 103, 104).ToList();
        var item = Assert.Single(Build(flat));
        Assert.Equal((Recommendations.Stalled, "low", Recommendations.Swap, Next), (item.Kind, item.Confidence, item.Action, item.SessionId!.Value));
        Assert.Contains("3", item.Why);
        Assert.Empty(Build(flat, pending: false));
        Assert.Empty(Build(flat.Take(5)));
        Assert.Empty(Build(Series("bench", 100, 102, 104, 104, 103, 106)));
        // Easier weeks are not evidence of a stall.
        Assert.Empty(Build(flat.Select(e => e with { Training = false })));
    }

    [Fact]
    public void RepeatedPainComesFirstAndReplacesTheStall()
    {
        var painful = Series("bench", 100, 100, 100, 100, 100, 100).Select((e, i) => e with { Pain = i >= 4 });
        var item = Assert.Single(Build(painful));
        Assert.Equal((Recommendations.RecurringPain, "medium"), (item.Kind, item.Confidence));
        // Once is not a pattern.
        Assert.Empty(Build([new ExerciseExposure("row", Start, null, true, true), new ExerciseExposure("row", Start.AddDays(3), null, false, true)]));
    }

    [Fact]
    public void DoingMuchLessThanPlannedIsSaid_ButNotOnSmallNumbers()
    {
        var item = Assert.Single(Build([], new RecentAdherence(80m, 30m)));
        Assert.Equal((Recommendations.LowAdherence, Recommendations.AdjustPlan), (item.Kind, item.Action));
        Assert.Contains("38", item.Why);
        Assert.Empty(Build([], new RecentAdherence(80m, 60m)));
        Assert.Empty(Build([], new RecentAdherence(10m, 0m)));
    }

    [Fact]
    public void TheFilledTextsAreTranslated()
    {
        var why = string.Format(Recommendations.PainWhy, 2, 4);
        Assert.Equal("You logged pain in 2 of the last 4 times.", Localizer.Translate(why, AppLanguage.En));
        Assert.Contains("38", Localizer.Translate(string.Format(Recommendations.AdherenceWhy, 38, 14), AppLanguage.En));
    }
}
