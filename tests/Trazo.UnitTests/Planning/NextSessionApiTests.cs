using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The backend decides which session comes next; clients only show it.</summary>
public sealed class NextSessionApiTests() : PlansApiBase(enforced: true)
{
    private static object ManualDays(DateOnly? startDate = null) => new
    {
        name = "Dos empujes", totalWeeks = 4, startDate, days = new object[]
        {
            new { day = "Monday", title = "Empuje A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4 } } },
            new { day = "Tuesday", title = "Empuje B", exercises = new object[] { new { exerciseId = "bench-press", sets = 4 } } },
            new { day = "Wednesday", title = "Pierna", exercises = new object[] { new { exerciseId = "squat", sets = 4 } } },
            new { day = "Friday", title = "Empuje C", exercises = new object[] { new { exerciseId = "bench-press", sets = 4 } } }
        }
    };

    private async Task<(HttpClient Client, List<JsonElement> Sessions)> StartManualAsync(bool premium, string email, DateOnly? startDate = null)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: premium));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualDays(startDate))).StatusCode);
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        var sessions = block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
        return (client, sessions);
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, object? performedAt = null, int rir = 0) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed", performedAt,
            sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32())
                .Select(n => new { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = 60m, repetitions = 8, rir, completed = true }))
                .ToArray()
        });

    private static Task<JsonElement> NextAsync(HttpClient client) => client.GetAsync("/api/v1/plans/current/next").ContinueWith(t => JsonAsync(t.Result)).Unwrap();

    [Fact]
    public async Task ItNeedsASignedInUser_AndAUserWithoutPlanGetsNotFound()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/plans/current/next")).StatusCode);
        var empty = ClientFor(await AddUserAsync("no-plan-next@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, (await empty.GetAsync("/api/v1/plans/current/next")).StatusCode);
    }

    [Fact]
    public async Task TheFirstPendingSessionIsNext_WithItsPositionInTheWeek()
    {
        var (client, sessions) = await StartManualAsync(premium: false, "next-first@example.com");
        var next = await NextAsync(client);
        Assert.Equal("Calendar", next.GetProperty("schedulingMode").GetString());
        Assert.Equal(sessions[0].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString());
        var position = next.GetProperty("position");
        Assert.Equal(1, position.GetProperty("period").GetInt32());
        Assert.Equal(1, position.GetProperty("sessionInPeriod").GetInt32());
        Assert.Equal(4, position.GetProperty("sessionsInPeriod").GetInt32());
    }

    [Fact]
    public async Task LoggingOutOfOrderDoesNotMoveTheProgram_TheNextIsStillTheFirstPending()
    {
        var (client, sessions) = await StartManualAsync(premium: false, "next-order@example.com");
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[2])).StatusCode); // "Pierna" first
        var next = await NextAsync(client);
        Assert.Equal(sessions[0].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString());
        Assert.Equal(1, next.GetProperty("position").GetProperty("sessionInPeriod").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(sessions[1].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());
    }

    [Fact]
    public async Task SessionsWhoseDatePassedAreListedAsOverdue_AndStayRegisterable()
    {
        var client = ClientFor(await AddUserAsync("next-overdue@example.com"));
        var started = new
        {
            name = "Retrasado", totalWeeks = 4, startDate = new DateOnly(2026, 11, 10),
            days = new object[] { new { day = "Tuesday", title = "A", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } } }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", started)).StatusCode);
        var next = await NextAsync(client);
        var overdue = next.GetProperty("overdue").EnumerateArray().ToList();
        Assert.Single(overdue); // Tuesday 10 Nov passed; the fixed clock is Monday 16 Nov
        Assert.Equal("2026-11-10", overdue[0].GetProperty("date").GetString());
        Assert.Equal(overdue[0].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString());
    }

    [Fact]
    public async Task WithoutPremiumTheSessionIsServedButTheRecoveryAdviceIsLocked()
    {
        var (client, sessions) = await StartManualAsync(premium: false, "next-free@example.com");
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        var next = await NextAsync(client);
        Assert.True(next.GetProperty("availabilityLocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, next.GetProperty("availability").ValueKind);
        Assert.Equal(JsonValueKind.Object, next.GetProperty("session").ValueKind);
    }

    [Fact]
    public async Task PremiumGetsTheRecoveryAdvice_ChestAgainAfterHeavyPressingMeansWait_AndTheLegSessionIsTheAlternative()
    {
        var (client, sessions) = await StartManualAsync(premium: true, "next-premium@example.com");
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-6), rir: 0)).StatusCode);

        var next = await NextAsync(client);
        Assert.False(next.GetProperty("availabilityLocked").GetBoolean());
        Assert.Equal(sessions[1].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString()); // the program says Empuje B
        var availability = next.GetProperty("availability");
        Assert.Equal("Wait", availability.GetProperty("state").GetString());
        Assert.Contains(availability.GetProperty("reasons").EnumerateArray(), r => r.GetString()!.StartsWith("Pecho"));
        Assert.True(availability.GetProperty("availableAt").GetDateTimeOffset() > Now);
        var alternative = Assert.Single(availability.GetProperty("alternatives").EnumerateArray());
        Assert.Equal(sessions[2].GetProperty("id").GetString(), alternative.GetProperty("sessionId").GetString()); // Pierna
    }

    [Fact]
    public async Task ThePremiumAdviceIsReadyAgainWhenTheWorkoutWasLongAgo()
    {
        var (client, sessions) = await StartManualAsync(premium: true, "next-rested@example.com", new DateOnly(2026, 11, 9));
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddDays(-5), rir: 2)).StatusCode);
        var availability = (await NextAsync(client)).GetProperty("availability");
        Assert.Equal("Ready", availability.GetProperty("state").GetString());
        Assert.Empty(availability.GetProperty("alternatives").EnumerateArray());
    }

    [Fact]
    public async Task WhenEverythingIsLoggedThereIsNoNextSession()
    {
        var client = ClientFor(await AddUserAsync("next-done@example.com"));
        var one = new
        {
            name = "Una", totalWeeks = 2, days = new object[] { new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } } }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", one)).StatusCode);
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        foreach (var session in block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray()))
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, session, rir: 2)).StatusCode);
        var next = await NextAsync(client);
        Assert.Equal(JsonValueKind.Null, next.GetProperty("session").ValueKind);
    }
}
