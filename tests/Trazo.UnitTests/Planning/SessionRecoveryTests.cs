using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The session recovery worked out from the muscle answers, for the app that no longer asks it when finishing.</summary>
public sealed class SessionRecoveryTests : PlansApiBase
{
    private static readonly AdjustableClock Clock = new(Now);

    public SessionRecoveryTests() : base(enforced: false, clock: Clock) => Clock.Now = Now;

    private static IReadOnlyDictionary<string, MuscleFeedbackInput> Answers(params (string Muscle, string? Recovery)[] answers) =>
        answers.ToDictionary(a => a.Muscle, a => new MuscleFeedbackInput(a.Recovery, null));

    private static readonly (IReadOnlyCollection<string>, int)[] Work =
        [(["Chest"], 4), (["Lats", "UpperBack"], 4), (["Biceps"], 2)];

    [Theory]
    [InlineData("Chest", "notRecovered", "Partial")] // 4 of 10 sets
    [InlineData("Biceps", "notRecovered", "Partial")]
    [InlineData("Chest", "recovered", "Good")]
    [InlineData("Chest", "justRecovered", "Good")]
    [InlineData("Chest", null, "Unknown")]
    public void TheSessionIsReadFromTheShareOfWorkNotRecovered(string muscle, string? recovery, string expected) =>
        Assert.Equal(expected, SessionRecovery.ForSession("Unknown", Answers((muscle, recovery)), Work));

    [Fact]
    public void HalfOfTheWorkNotRecoveredIsAnInsufficientSession() =>
        Assert.Equal("Insufficient", SessionRecovery.ForSession(
            "Unknown", Answers(("Chest", "notRecovered"), ("Biceps", "notRecovered")), Work));

    [Theory]
    [InlineData("Good")]
    [InlineData("Partial")]
    [InlineData("Insufficient")]
    public void WhatAnOlderAppReportsWins(string reported)
    {
        var answers = Answers(("Chest", "notRecovered"));
        Assert.Equal(reported, SessionRecovery.ForSession(reported, answers, Work));
        Assert.Equal(reported, SessionRecovery.ForExercise(reported, answers, ["Chest"]));
    }

    [Fact]
    public void AnExerciseIsReadFromItsOwnMuscles()
    {
        var answers = Answers(("Chest", "notRecovered"), ("Lats", "recovered"));
        Assert.Equal("Insufficient", SessionRecovery.ForExercise("Unknown", answers, ["Chest"]));
        Assert.Equal("Good", SessionRecovery.ForExercise("Unknown", answers, ["Lats", "UpperBack"]));
        Assert.Equal("Unknown", SessionRecovery.ForExercise("Unknown", answers, ["Biceps"]));
    }

    // ------------------------------------------------------------------ through the API

    private static object Day(string day) => new
    {
        day, title = day, exercises = new object[] { new { exerciseId = "bench-press", sets = 4, minRepetitions = 6, maxRepetitions = 10, targetRir = 2 } }
    };

    private async Task<HttpClient> StartAsync(string email)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: true));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual",
            new { name = "Dos veces", totalWeeks = 4, days = new[] { Day("Monday"), Day("Thursday") } });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<JsonElement> LogNextAsync(HttpClient client, string? recovery, string? muscleRecovery)
    {
        var session = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"))).GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .First(s => s.GetProperty("log").ValueKind == JsonValueKind.Null);
        Clock.Now = Clock.Now.AddDays(2);
        var count = session.GetProperty("exercises")[0].GetProperty("sets").GetInt32();
        var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed", recovery,
            sets = Enumerable.Range(1, count).Select(n => new
                { exerciseId = "bench-press", setNumber = n, weightKg = 60, repetitions = 8, rir = 2, completed = true }).ToArray(),
            muscleFeedback = muscleRecovery is null ? null : new Dictionary<string, object> { ["Chest"] = new { recovery = muscleRecovery } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("adaptation");
    }

    /// <summary>The same two bad days, told the old way and the new way, are read the same way.</summary>
    [Theory]
    [InlineData("Insufficient", null)]
    [InlineData(null, "notRecovered")]
    public async Task BadDaysPauseProgressionHoweverTheyAreTold(string? recovery, string? muscleRecovery)
    {
        var client = await StartAsync($"recovery-{recovery}-{muscleRecovery}@example.com");

        var first = await LogNextAsync(client, recovery, muscleRecovery);
        Assert.Equal("HoldProgressionAndReview", first.GetProperty("sessionAction").GetString());
        var second = await LogNextAsync(client, recovery, muscleRecovery);
        Assert.Equal("ReviewAndReduceTrainingStress", second.GetProperty("sessionAction").GetString());
        var cuts = second.GetProperty("applied").EnumerateArray()
            .Count(a => a.GetProperty("kind").GetString()!.Contains("reduc", StringComparison.Ordinal));
        Assert.True(cuts <= 1, "one workout takes at most one set from the exercise");
    }

    [Fact]
    public async Task NothingDerivedIsStoredAndARecoveredMuscleHoldsNothing()
    {
        var client = await StartAsync("recovery-stored@example.com");

        var adaptation = await LogNextAsync(client, null, "recovered");
        Assert.Equal("NoChange", adaptation.GetProperty("sessionAction").GetString());

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.AsNoTracking().SingleAsync();
        Assert.Equal("Unknown", stored.Recovery);
    }
}
