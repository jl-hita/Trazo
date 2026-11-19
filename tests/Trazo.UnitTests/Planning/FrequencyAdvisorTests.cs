using System.Net.Http.Json;
using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The pace the user really keeps against the one the plan was built for. Informative only: it never rewrites the plan.</summary>
public sealed class FrequencyAdvisorTests
{
    [Theory]
    [InlineData(4, 4.0, FrequencyState.OnTrack)]
    [InlineData(4, 3.5, FrequencyState.OnTrack)]
    [InlineData(4, 3.1, FrequencyState.OnTrack)]
    [InlineData(4, 3.0, FrequencyState.Below)]
    [InlineData(4, 1.5, FrequencyState.Below)]
    [InlineData(3, 4.0, FrequencyState.Above)]
    [InlineData(3, 3.9, FrequencyState.OnTrack)]
    public void ComparesThePaceWithAOneSessionTolerance(int nominal, double observed, FrequencyState expected) =>
        Assert.Equal(expected, FrequencyAdvisor.Advise(false, nominal, (decimal)observed).State);

    [Fact]
    public void WithoutEvidenceNothingIsSaid()
    {
        var advice = FrequencyAdvisor.Advise(false, 4, null);
        Assert.Equal(FrequencyState.Unknown, advice.State);
        Assert.Null(advice.Observed);
        Assert.Null(advice.SuggestedSessionsPerWeek);
    }

    [Fact]
    public void ARotationHasNoCalendarSoItsPaceIsNeverJudged()
    {
        var advice = FrequencyAdvisor.Advise(true, 4, 1.0m);
        Assert.Equal(FrequencyState.Unknown, advice.State);
        Assert.Null(advice.SuggestedSessionsPerWeek);
    }

    [Theory]
    [InlineData(5, 3.2, 3)]
    [InlineData(4, 2.6, 3)]
    [InlineData(4, 1.0, 2)]
    [InlineData(3, 2.0, 2)]
    public void ALowerPaceSuggestsAFrequencyBetweenTwoAndOneBelowThePlan(int nominal, double observed, int suggested)
    {
        var advice = FrequencyAdvisor.Advise(false, nominal, (decimal)observed);
        Assert.Equal(FrequencyState.Below, advice.State);
        Assert.Equal(suggested, advice.SuggestedSessionsPerWeek);
        Assert.InRange(advice.SuggestedSessionsPerWeek!.Value, FrequencyAdvisor.MinimumSuggestedSessionsPerWeek, nominal - 1);
    }

    [Fact]
    public void OnlyABelowPaceSuggestsAChange()
    {
        Assert.Null(FrequencyAdvisor.Advise(false, 3, 4.5m).SuggestedSessionsPerWeek);
        Assert.Null(FrequencyAdvisor.Advise(false, 3, 3.0m).SuggestedSessionsPerWeek);
    }

    [Fact]
    public void ThePaceFeedsFromLoadMetricsEvidenceRules()
    {
        var now = new DateTimeOffset(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);
        // Three workouts are not enough evidence; four spread over two weeks are.
        var few = new[] { now.AddDays(-14), now.AddDays(-7), now.AddDays(-1) };
        Assert.Equal(FrequencyState.Unknown, FrequencyAdvisor.Advise(false, 4, LoadMetrics.ObservedSessionsPerSevenDays(few, now)).State);
        var enough = new[] { now.AddDays(-14), now.AddDays(-10), now.AddDays(-5), now.AddDays(-1) };
        var advice = FrequencyAdvisor.Advise(false, 4, LoadMetrics.ObservedSessionsPerSevenDays(enough, now));
        Assert.Equal(FrequencyState.Below, advice.State);
        Assert.Equal(2, advice.SuggestedSessionsPerWeek); // 4 workouts in 13 days is about 1.6 a week
    }
}

public sealed class FrequencyApiTests() : PlansApiBase(enforced: false)
{
    [Fact]
    public async Task ANewPlanReportsAnUnknownPaceAndItsNominalFrequency()
    {
        var (_, client, _) = await StartPlanAsync();
        var next = await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));
        var frequency = next.GetProperty("frequency");
        Assert.Equal("Unknown", frequency.GetProperty("state").GetString());
        Assert.Equal(4, frequency.GetProperty("nominal").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(frequency.GetProperty("message").GetString()));
    }
}
