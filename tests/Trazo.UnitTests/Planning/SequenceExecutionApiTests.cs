using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The sequence mode as the real service runs it: the rotation continues from what is persisted, whatever the user does with the order and
/// the days, and the load view tells a rotation apart from real time. The clock is fixed at Monday 2026-11-16 08:00 UTC; the plan starts a
/// week before, so a calendar plan would be in its second week.
/// </summary>
public sealed class SequenceExecutionApiTests() : PlansApiBase(enforced: true)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly string[] PushPullLegsCycle = ["Empuje", "Tracción", "Pierna", "Empuje B", "Tracción B", "Pierna B"];

    private static object Request(string split, int perPeriod, string goal = "Hypertrophy") => new
    {
        goal, experience = "Intermediate", totalWeeks = 12, startDate = new DateOnly(2026, 11, 9), schedulingMode = "Sequence", split,
        sessionsPerPeriod = perPeriod, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>()
    };

    private async Task<HttpClient> StartAsync(string email, string split, int perPeriod)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: true));
        var created = await client.PostAsJsonAsync("/api/v1/plans", Request(split, perPeriod));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<List<JsonElement>> BlockAsync(HttpClient client)
    {
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        return block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
    }

    private static async Task<JsonElement> NextAsync(HttpClient client) => await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));

    private static string TitleOf(JsonElement session) => session.GetProperty("title").GetString()!;

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, DateTimeOffset performedAt)
    {
        var sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32())
            .Select(n => new { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = 60m, repetitions = 8, rir = 2, completed = true })).ToArray();
        return client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets, performedAt });
    }

    private static Task<HttpResponseMessage> SkipAsync(HttpClient client, JsonElement session, DateTimeOffset performedAt) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "NotCompleted", nonCompletionReason = "Time", sets = Array.Empty<object>(), performedAt });

    // ------------------------------------------------------------------ continuity

    [Fact]
    public async Task AnOddRotationContinuesAcrossPeriodsAndBlocks_WhateverTheUserDoes()
    {
        // Four sessions of push/pull/legs per period: the rotation (six sessions) never lines up with a period or a block.
        var client = await StartAsync("seq-oddrotation@example.com", "PushPullLegs", 4);
        var first = await BlockAsync(client);
        Assert.Equal(16, first.Count);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => PushPullLegsCycle[i % 6]), first.Select(TitleOf));

        // Irregular execution: three days in a row, a skipped session, a long gap, a session registered out of order.
        DateTimeOffset[] at = [Start, Start.AddDays(1), Start.AddDays(2), Start.AddDays(2).AddHours(3), Start.AddDays(5), Start.AddDays(5).AddHours(2)];
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.OK, (i == 3 ? await SkipAsync(client, first[i], at[i]) : await LogAsync(client, first[i], at[i])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, first[7], Start.AddDays(6))).StatusCode); // out of order: the 8th before the 7th
        for (var i = 6; i < 16; i++)
            if (i != 7) Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, first[i], Start.AddDays(6).AddMinutes(30 * (i + 1)))).StatusCode);

        // The block is resolved: the next one is generated and starts where the rotation was (session 17 = position 4 = "Tracción B").
        var next = await NextAsync(client);
        Assert.Equal(17, next.GetProperty("session").GetProperty("sequenceIndex").GetInt32());
        Assert.Equal("Tracción B", TitleOf(next.GetProperty("session")));
        Assert.Equal(5, next.GetProperty("position").GetProperty("period").GetInt32());
        var second = await BlockAsync(client);
        Assert.Equal(["Tracción B", "Pierna B", "Empuje", "Tracción"], second.Select(TitleOf)); // the deload period goes on with the rotation
        Assert.True(next.GetProperty("position").GetProperty("mesocycle").GetProperty("isDeload").GetBoolean());
    }

    [Fact]
    public async Task ASkippedSessionDoesNotRestartTheRotation_TheNextOneIsTheFollowingOne()
    {
        var client = await StartAsync("seq-skip@example.com", "PushPullLegs", 3);
        var block = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, block[0], Start)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, block[1], Start.AddDays(1))).StatusCode);
        var next = await NextAsync(client);
        Assert.Equal("Pierna", TitleOf(next.GetProperty("session")));
        Assert.Equal(3, next.GetProperty("session").GetProperty("sequenceIndex").GetInt32());
        Assert.Equal(0, next.GetProperty("overdue").GetArrayLength());
    }

    [Fact]
    public async Task ASessionDoneOutOfOrderDoesNotMoveTheProgram_AndWhenTheGapIsFilledItContinuesAfterTheLastDone()
    {
        var client = await StartAsync("seq-order@example.com", "UpperLower", 3);
        var block = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, block[2], Start.AddDays(1))).StatusCode); // the third first
        Assert.Equal(block[0].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, block[0], Start.AddDays(2))).StatusCode);
        Assert.Equal(block[1].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, block[1], Start.AddDays(3))).StatusCode);
        var next = await NextAsync(client);
        Assert.Equal(4, next.GetProperty("session").GetProperty("sequenceIndex").GetInt32());
        Assert.Equal("Tren inferior B", TitleOf(next.GetProperty("session"))); // the fourth of the rotation: period 2 does not restart it
    }

    // ------------------------------------------------------------------ the load view: rotation versus real time

    [Fact]
    public async Task TheLoadViewSeparatesTheVolumeOfARotationFromTheDoseOfRealTime()
    {
        var fast = await StartAsync("seq-fast@example.com", "FullBody", 3);
        var slow = await StartAsync("seq-slow@example.com", "FullBody", 3);
        var fastSessions = await BlockAsync(fast);
        var slowSessions = await BlockAsync(slow);

        // The same four sessions: a trainee who does them in six days and another who takes seven.
        DateTimeOffset[] fastAt = [Now.AddDays(-6), Now.AddDays(-5), Now.AddDays(-3), Now.AddHours(-1)];
        DateTimeOffset[] slowAt = [Now.AddDays(-7), Now.AddDays(-5.5), Now.AddDays(-3.5), Now.AddHours(-1)];
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(fast, fastSessions[i], fastAt[i])).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(slow, slowSessions[i], slowAt[i])).StatusCode);
        }

        var fastLoad = await JsonAsync(await fast.GetAsync("/api/v1/plans/current/load"));
        var slowLoad = await JsonAsync(await slow.GetAsync("/api/v1/plans/current/load"));
        Assert.Equal("Rotation", fastLoad.GetProperty("periodKind").GetString());

        // Per rotation both did exactly what was prescribed: the three sessions of the first period, whatever the days they took.
        JsonElement Period(JsonElement load, int period) => load.GetProperty("weeks").EnumerateArray().First(w => w.GetProperty("week").GetInt32() == period);
        foreach (var load in new[] { fastLoad, slowLoad })
        {
            Assert.Equal(Period(load, 1).GetProperty("plannedSets").GetInt32(), Period(load, 1).GetProperty("completedSets").GetInt32());
            Assert.Equal(3, Period(load, 1).GetProperty("loggedSessions").GetInt32());
            Assert.Equal(1, Period(load, 2).GetProperty("loggedSessions").GetInt32());
        }

        // In real time they did not: the pace measured between their workouts differs, and the nominal pace is the planned one.
        var fastRolling = fastLoad.GetProperty("rolling");
        var slowRolling = slowLoad.GetProperty("rolling");
        Assert.Equal(3, fastRolling.GetProperty("nominalSessionsPer7Days").GetInt32());
        Assert.Equal(4, fastRolling.GetProperty("workouts").GetInt32());
        Assert.Equal(3, slowRolling.GetProperty("workouts").GetInt32()); // the first one fell out of the seven-day window
        Assert.True(fastRolling.GetProperty("observedSessionsPer7Days").GetDecimal() > slowRolling.GetProperty("observedSessionsPer7Days").GetDecimal());
        Assert.NotEmpty(fastRolling.GetProperty("actual").EnumerateArray());
        Assert.NotEmpty(fastRolling.GetProperty("prescribedPer7Days").EnumerateArray());
    }

    [Fact]
    public async Task WithTooFewWorkoutsThePaceIsReportedAsUnknown()
    {
        var client = await StartAsync("seq-pace@example.com", "FullBody", 3);
        var block = await BlockAsync(client);
        await LogAsync(client, block[0], Now.AddDays(-2));
        await LogAsync(client, block[1], Now.AddDays(-1));
        var rolling = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/load"))).GetProperty("rolling");
        Assert.Equal(2, rolling.GetProperty("workouts").GetInt32());
        Assert.Equal(JsonValueKind.Null, rolling.GetProperty("observedSessionsPer7Days").ValueKind);
    }

    [Fact]
    public async Task ACalendarPlanReportsItsPeriodsAsWeeks()
    {
        var client = ClientFor(await AddUserAsync("seq-calendar-load@example.com", subscribed: true));
        var created = await client.PostAsJsonAsync("/api/v1/plans", new
        {
            goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12, startDate = new DateOnly(2026, 11, 9),
            trainingDays = new[] { "Monday", "Wednesday", "Friday" }, maxSessionMinutes = 75,
            availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var load = await JsonAsync(await client.GetAsync("/api/v1/plans/current/load"));
        Assert.Equal("Week", load.GetProperty("periodKind").GetString());
    }

    // ------------------------------------------------------------------ contract

    [Fact]
    public async Task TheOptionsSayWhichCombinationsEachSplitSupportsInEachMode()
    {
        var client = ClientFor(await AddUserAsync("seq-options@example.com", subscribed: true));
        var options = await JsonAsync(await client.GetAsync("/api/v1/plans/options"));
        var support = options.GetProperty("splitSupport").EnumerateArray().ToDictionary(s => s.GetProperty("split").GetString()!);
        int[] Ints(string split, string field) => support[split].GetProperty(field).EnumerateArray().Select(v => v.GetInt32()).ToArray();

        Assert.Equal([3, 5, 6], Ints("PushPullLegs", "calendarDays"));
        Assert.Equal([3, 4, 5, 6], Ints("PushPullLegs", "sequenceSessions"));
        Assert.Equal([2, 4], Ints("UpperLower", "calendarDays"));
        Assert.Equal([2, 3, 4, 5, 6], Ints("UpperLower", "sequenceSessions"));
        Assert.Equal([2, 3, 4], Ints("FullBody", "calendarDays"));
        Assert.Equal([2, 3, 4, 5, 6], Ints("FullBody", "sequenceSessions"));
        Assert.Equal([2, 3, 4], Ints("Automatic", "calendarDays"));
        Assert.Equal([2, 3, 4, 5, 6], Ints("Automatic", "sequenceSessions"));
    }

    [Fact]
    public async Task ASequenceWithAnUnsupportedSplitFailsWithTheEnginesExplanation()
    {
        var client = ClientFor(await AddUserAsync("seq-unsupported@example.com", subscribed: true));
        var response = await client.PostAsJsonAsync("/api/v1/plans", Request("PushPullLegs", 2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("plan_generation_failed", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task ACalendarPlanKeepsItsClassicalRestrictions()
    {
        var client = ClientFor(await AddUserAsync("seq-classical@example.com", subscribed: true));
        var response = await client.PostAsJsonAsync("/api/v1/plans", new
        {
            goal = "Hypertrophy", experience = "Intermediate", split = "UpperLower", trainingDays = new[] { "Monday", "Wednesday", "Friday" },
            availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
