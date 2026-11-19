using System.Net;
using System.Net.Http.Json;
using Trazo.Application.Planning;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Swapping an exercise keeps what the user did as a reference, never as a converted load.</summary>
public sealed class SwapContinuityTests
{
    private static LastPerformanceView Last(decimal? suggested = 62.5m, string? basis = "increase", int? sessions = 3) => new(
        new DateOnly(2026, 11, 16), [new LastSetView(1, 60m, 8, 2m), new LastSetView(2, 60m, 8, 2m)], suggested, basis, sessions);

    [Fact]
    public void TheReferenceKeepsTheSetsAndNeverProposesALoad()
    {
        var reference = SwapContinuity.Reference(Last(), replacementHasHistory: false)!;
        Assert.Null(reference.SuggestedLoadKg);
        Assert.Equal(SwapContinuity.ReferenceBasis, reference.SuggestionBasis);
        Assert.Null(reference.BasedOnSessions);
        Assert.Equal(2, reference.Sets.Count);
        Assert.Equal(60m, reference.Sets[0].WeightKg);
    }

    [Fact]
    public void OnceTheReplacementHasItsOwnHistoryTheReferenceDisappears() =>
        Assert.Null(SwapContinuity.Reference(Last(), replacementHasHistory: true));

    [Fact]
    public void NothingDoneWithTheOriginalMeansNoReference() =>
        Assert.Null(SwapContinuity.Reference(null, replacementHasHistory: false));
}

public sealed class SwapContinuityApiTests() : PlansApiBase(enforced: false)
{
    [Fact]
    public async Task ASwappedExerciseShowsWhatWasDoneWithTheOriginalAsAReferenceOnly()
    {
        var (_, client, week) = await StartPlanAsync();
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        var first = sessions[0];
        var doneIds = first.GetProperty("exercises").EnumerateArray().Select(e => e.GetProperty("exerciseId").GetString()!).ToHashSet();
        var later = sessions.Skip(1).FirstOrDefault(s => s.GetProperty("exercises").EnumerateArray().Any(e => doneIds.Contains(e.GetProperty("exerciseId").GetString()!)));
        Assert.True(later.ValueKind != System.Text.Json.JsonValueKind.Undefined, "an exercise of the first session repeats later in the week");
        var target = later.GetProperty("exercises").EnumerateArray().First(e => doneIds.Contains(e.GetProperty("exerciseId").GetString()!)).GetProperty("exerciseId").GetString()!;

        var logged = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{first.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", recovery = "Good", sets = CompleteSets(first, 40m, 10) });
        Assert.True(logged.StatusCode == HttpStatusCode.OK, await logged.Content.ReadAsStringAsync());

        var laterId = later.GetProperty("id").GetString();
        var listing = await client.GetAsync($"/api/v1/plans/current/sessions/{laterId}/exercises/{target}/alternatives");
        var replacement = (await JsonAsync(listing)).GetProperty("alternatives")[0].GetProperty("exerciseId").GetString();
        var swapped = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{laterId}/exercises/{target}/swap", new { replacementExerciseId = replacement });
        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);

        var refreshed = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var session = refreshed.GetProperty("sessions").EnumerateArray().First(s => s.GetProperty("id").GetString() == laterId);
        var exercise = session.GetProperty("exercises").EnumerateArray().First(e => e.GetProperty("exerciseId").GetString() == replacement);
        Assert.Equal(target, exercise.GetProperty("swappedFromExerciseId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, exercise.GetProperty("lastPerformance").ValueKind);
        var reference = exercise.GetProperty("replacedPerformance");
        Assert.Equal("reference", reference.GetProperty("suggestionBasis").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reference.GetProperty("suggestedLoadKg").ValueKind);
        Assert.Equal(40m, reference.GetProperty("sets")[0].GetProperty("weightKg").GetDecimal());
        // The replacement carries no load of its own: its first exposure is a normal first exposure.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, exercise.GetProperty("targetLoadKg").ValueKind);
    }
}
