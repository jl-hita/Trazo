using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Planning is not execution: a session keeps its identity (and its place in the program) whenever the user logs it, and the
/// engine reads WHEN the workout happened (PerformedAt), not when it was registered or when it was planned. The plan starts
/// a week before the fixed clock (Monday 2026-11-16) so week 1 is in the past and week 2 is the current one.
/// </summary>
public sealed class FlexibleRegistrationTests() : PlansApiBase(enforced: false)
{
    private static readonly DateOnly PlanStart = new(2026, 11, 9);

    private static object ProfileStartedLastWeek() => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12, startDate = PlanStart,
        trainingDays = new[] { "Monday", "Tuesday", "Thursday", "Friday" }, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>()
    };

    /// <summary>Every session of the current block, in program order (SequenceIndex).</summary>
    private static async Task<List<JsonElement>> BlockSessionsAsync(HttpClient client)
    {
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        return block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
    }

    private async Task<(HttpClient Client, List<JsonElement> Sessions)> StartAsync(string email = "flex@example.com", string? timeZone = null)
    {
        var (_, client, _) = await StartPlanAsync(email, ProfileStartedLastWeek());
        if (timeZone is not null) client.DefaultRequestHeaders.Add("X-Time-Zone", timeZone);
        return (client, await BlockSessionsAsync(client));
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, object? performedAt = null, Guid? clientRequestId = null,
        bool stressed = false)
    {
        var sets = stressed
            ? session.GetProperty("exercises").EnumerateArray().Select(e => (object)new
            {
                exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = 1, weightKg = 50m, repetitions = 6, rir = 0, completed = true
            }).ToArray()
            : (object[])CompleteSets(session);
        var body = new Dictionary<string, object?>
        {
            ["outcome"] = stressed ? "Partial" : "Completed", ["sets"] = sets, ["performedAt"] = performedAt, ["clientRequestId"] = clientRequestId
        };
        if (stressed) { body["nonCompletionReason"] = "Fatigue"; body["recovery"] = "Insufficient"; body["sessionRpe"] = 10; }
        return client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", body);
    }

    private async Task<List<PlanAdaptation>> AuditAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanAdaptations.AsNoTracking().ToListAsync();
    }

    // ------------------------------------------------------------------ identity and order of the program

    [Fact]
    public async Task EverySessionHasItsPositionInTheProgram_InOrderAndWithoutGaps()
    {
        var (_, sessions) = await StartAsync();
        Assert.True(sessions.Count >= 8);
        Assert.Equal(Enumerable.Range(1, sessions.Count), sessions.Select(s => s.GetProperty("sequenceIndex").GetInt32()));
        var dates = sessions.Select(s => s.GetProperty("date").GetString()).ToList();
        Assert.Equal(dates.OrderBy(d => d, StringComparer.Ordinal), dates);
    }

    [Fact]
    public async Task ALaterBlockOfACalendarPlanIsGeneratedWhenItsWeekIsReached_ContinuingTheProgramNumbering()
    {
        var (client, sessions) = await StartAsync("later-block@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            // Three more weeks pass. Phases are periods, so only the start date moves.
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var plan = await db.TrainingPlans.SingleAsync();
            plan.StartDate = plan.StartDate.AddDays(-21); // started a week ago, so week 5 is reached
            await db.SaveChangesAsync();
        }
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(5, week.GetProperty("weekNumber").GetInt32());
        Assert.Equal(2, week.GetProperty("mesocycle").GetProperty("sequence").GetInt32());
        Assert.True(week.GetProperty("mesocycle").GetProperty("isDeload").GetBoolean()); // week 5 of 12 is a deload
        var next = week.GetProperty("sessions").EnumerateArray().Select(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
        Assert.NotEmpty(next);
        Assert.True(next.Min() > sessions.Max(s => s.GetProperty("sequenceIndex").GetInt32()));
    }

    // ------------------------------------------------------------------ real date vs registration date

    [Fact]
    public async Task TheRealDateOfTheWorkoutIsKeptApartFromTheMomentItWasRegistered()
    {
        var (client, sessions) = await StartAsync();
        var performed = Now.AddDays(-3).AddHours(2); // Friday 13 Nov, 10:00 UTC
        var response = await LogAsync(client, sessions[0], performed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await JsonAsync(response)).GetProperty("session");
        var log = session.GetProperty("log");
        Assert.Equal(performed, log.GetProperty("performedAt").GetDateTimeOffset());
        Assert.Equal(Now, log.GetProperty("loggedAt").GetDateTimeOffset());
        Assert.Equal("2026-11-13", log.GetProperty("performedOn").GetString());
        Assert.Equal(sessions[0].GetProperty("id").GetString(), session.GetProperty("id").GetString()); // same identity
        Assert.Equal(1, session.GetProperty("sequenceIndex").GetInt32());
        Assert.Equal(sessions[0].GetProperty("date").GetString(), session.GetProperty("date").GetString()); // planned date untouched
    }

    [Fact]
    public async Task WithoutADateTheWorkoutHappenedWhenItWasRegistered()
    {
        var (client, sessions) = await StartAsync();
        var log = (await JsonAsync(await LogAsync(client, sessions[0]))).GetProperty("session").GetProperty("log");
        Assert.Equal(Now, log.GetProperty("performedAt").GetDateTimeOffset());
        Assert.Equal("2026-11-16", log.GetProperty("performedOn").GetString());
    }

    [Fact]
    public async Task AWorkoutCannotHappenInTheFuture_NorBeforeThePlanStarted()
    {
        var (client, sessions) = await StartAsync();
        var future = await LogAsync(client, sessions[0], Now.AddHours(2));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("performedAt", await future.Content.ReadAsStringAsync());

        var beforeStart = await LogAsync(client, sessions[0], Now.AddDays(-30));
        Assert.Equal(HttpStatusCode.BadRequest, beforeStart.StatusCode);

        var small = await LogAsync(client, sessions[0], Now.AddMinutes(3)); // clock drift is tolerated and clamped
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
        Assert.Equal(Now, (await JsonAsync(small)).GetProperty("session").GetProperty("log").GetProperty("performedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("Europe/Madrid", "2026-11-16")]       // 23:30 UTC on the 15th is already past midnight in Spain
    [InlineData("America/Los_Angeles", "2026-11-15")] // and still the evening of the 15th in California
    public async Task TheDayOfTheWorkoutFollowsTheTimeZoneOfTheUser_AroundMidnight(string zone, string expectedDay)
    {
        var (client, sessions) = await StartAsync(timeZone: zone);
        var log = (await JsonAsync(await LogAsync(client, sessions[0], new DateTimeOffset(2026, 11, 15, 23, 30, 0, TimeSpan.Zero))))
            .GetProperty("session").GetProperty("log");
        Assert.Equal(expectedDay, log.GetProperty("performedOn").GetString());
    }

    // ------------------------------------------------------------------ no window: late, early and out of order

    [Fact]
    public async Task ASessionCanBeRegisteredHoweverLate_ThereIsNoTwoWeekWindow()
    {
        var (client, sessions) = await StartAsync();
        // Make the session look months old for the old window rule (current week or the one before).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var id = Guid.Parse(sessions[0].GetProperty("id").GetString()!);
            var row = await db.PlannedSessions.FirstAsync(s => s.Id == id);
            row.WeekNumber = -9;
            await db.SaveChangesAsync();
        }
        var response = await LogAsync(client, sessions[0], Now.AddDays(-6));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SessionsCanBeLoggedEarlyAndOutOfOrder_EachKeepsItsOwnIdentity()
    {
        var (client, sessions) = await StartAsync();
        // The fourth session of the program first, then the second: nothing is renumbered or merged.
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[3], Now.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[1], Now.AddDays(-5))).StatusCode);
        var after = await BlockSessionsAsync(client);
        Assert.Equal(sessions.Select(s => s.GetProperty("id").GetString()), after.Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(["Planned", "Completed", "Planned", "Completed"], after.Take(4).Select(s => s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ConsecutiveDaysAndSeveralSessionsTheSameDayAreAllRecorded()
    {
        var (client, sessions) = await StartAsync();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[i], Now.AddDays(-3 + i))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[3], Now.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[4], Now.AddDays(-1).AddHours(3))).StatusCode); // two the same day
    }

    [Fact]
    public async Task OmittedAndPartialSessionsAreResolvedWithoutExecution()
    {
        var (client, sessions) = await StartAsync();
        var omitted = await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{sessions[0].GetProperty("id").GetString()}/log",
            new { outcome = "NotCompleted", nonCompletionReason = "Time", sets = Array.Empty<object>(), performedAt = Now.AddDays(-2) });
        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
        Assert.Equal("Skipped", (await JsonAsync(omitted)).GetProperty("session").GetProperty("status").GetString());
        var partial = await LogAsync(client, sessions[1], Now.AddDays(-1), stressed: false);
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
    }

    [Fact]
    public async Task TheHistoryFollowsWhenTheWorkoutsHappened_NotTheOrderTheyWereRegistered()
    {
        var (client, sessions) = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[1], Now.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddDays(-4))).StatusCode); // registered second, happened first
        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/history"));
        Assert.Equal([sessions[1].GetProperty("id").GetString(), sessions[0].GetProperty("id").GetString()],
            history.EnumerateArray().Select(h => h.GetProperty("sessionId").GetString()));
    }

    // ------------------------------------------------------------------ adaptation on the timeline of the evidence

    [Fact]
    public async Task ThreeStressedWorkoutsInOrderStillTriggerTheDeload_WhateverDayTheyHappened()
    {
        var (client, sessions) = await StartAsync();
        JsonElement last = default;
        for (var i = 0; i < 3; i++)
            last = await JsonAsync(await LogAsync(client, sessions[i], Now.AddDays(-6 + i), stressed: true));
        var applied = last.GetProperty("adaptation").GetProperty("applied").EnumerateArray().ToList();
        Assert.Contains(applied, a => a.GetProperty("kind").GetString() == "deload");
        var audit = Assert.Single(await AuditAsync(), a => a.Kind == "deload");
        Assert.Equal(Now.AddDays(-4), audit.EvidenceAt); // cooldowns run on when it happened, not on the registration
        Assert.Equal(Now, audit.AppliedAt);
    }

    [Fact]
    public async Task ALateRegisteredWorkoutOlderThanOthersAlreadySavedNeverChangesThePlan()
    {
        var (client, sessions) = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[3], Now.AddDays(-1))).StatusCode); // newest evidence
        var beforeLate = await BlockSessionsAsync(client); // includes the load carried over by that first log
        JsonElement last = default;
        for (var i = 0; i < 3; i++) // the same stressed trio, but they happened BEFORE what is already saved
            last = await JsonAsync(await LogAsync(client, sessions[i], Now.AddDays(-6 + i), stressed: true));
        var report = last.GetProperty("adaptation");
        Assert.Empty(report.GetProperty("applied").EnumerateArray());
        Assert.Contains(report.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("anterior"));
        Assert.Empty(await AuditAsync());
        var after = await BlockSessionsAsync(client);
        Assert.Equal(beforeLate.Skip(4).Select(s => s.GetProperty("exercises").ToString()), after.Skip(4).Select(s => s.GetProperty("exercises").ToString()));
    }

    [Fact]
    public async Task AdaptationOnlyTouchesSessionsAfterTheFurthestPointOfTheProgram_AndNeverTheOnesAlreadyDone()
    {
        var (client, sessions) = await StartAsync();
        // The second session is forgotten; the program continues with the third, fourth and fifth (stressed).
        var snapshots = new Dictionary<int, string>();
        foreach (var i in new[] { 0, 2, 3, 4 })
        {
            snapshots[i] = (await BlockSessionsAsync(client))[i].GetProperty("exercises").ToString(); // what was prescribed when it was done
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[i], Now.AddDays(-6 + i), stressed: i > 0)).StatusCode);
        }

        var after = await BlockSessionsAsync(client);
        // Executed sessions keep exactly what was prescribed when they were done: no later adaptation rewrites them.
        foreach (var (i, exercises) in snapshots)
            Assert.Equal(exercises, after[i].GetProperty("exercises").ToString());
        Assert.Contains(await AuditAsync(), a => a.Kind == "deload");
        // The forgotten session lies BEFORE the furthest point of the program: the deload does not reach it.
        Assert.DoesNotContain(after[1].GetProperty("exercises").EnumerateArray(), e => e.GetProperty("progressionRule").GetString() == "None");
        // The sessions that come next do get it.
        Assert.Contains(after[5].GetProperty("exercises").EnumerateArray(), e => e.GetProperty("progressionRule").GetString() == "None");
    }

    // ------------------------------------------------------------------ retries, duplicates and concurrency

    [Fact]
    public async Task ARetryWithTheSameRequestKeyReturnsTheSavedResult_EvenIfTheBodyChanged()
    {
        var (client, sessions) = await StartAsync();
        var key = Guid.NewGuid();
        var first = await LogAsync(client, sessions[0], Now.AddDays(-2), key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False((await JsonAsync(first)).GetProperty("replayed").GetBoolean());

        var retry = await LogAsync(client, sessions[0], Now.AddDays(-2), key);
        Assert.True((await JsonAsync(retry)).GetProperty("replayed").GetBoolean());
        var changed = await LogAsync(client, sessions[0], Now.AddDays(-1), key, stressed: true); // same key = same request
        Assert.True((await JsonAsync(changed)).GetProperty("replayed").GetBoolean());

        var otherSession = await LogAsync(client, sessions[1], Now.AddDays(-1), key); // the key belongs to another session
        Assert.Equal(HttpStatusCode.BadRequest, otherSession.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.CountAsync());
    }

    [Fact]
    public async Task ADifferentLogForAnAlreadyLoggedSessionIsAConflict_AndIdenticalOnesAreReplayed()
    {
        var (client, sessions) = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddDays(-2))).StatusCode);
        Assert.True((await JsonAsync(await LogAsync(client, sessions[0], Now.AddDays(-2)))).GetProperty("replayed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await LogAsync(client, sessions[0], Now.AddDays(-2), stressed: true)).StatusCode);
    }

    [Fact]
    public async Task TwoIdenticalRequestsAtOnceLeaveOneLog_AndBothSucceed()
    {
        var (client, sessions) = await StartAsync();
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(
            LogAsync(client, sessions[0], Now.AddDays(-2), key), LogAsync(client, sessions[0], Now.AddDays(-2), key));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.CountAsync());
    }

    [Fact]
    public async Task AnInvalidRequestKeyIsRejected()
    {
        var (client, sessions) = await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await LogAsync(client, sessions[0], clientRequestId: Guid.Empty)).StatusCode);
    }
}
