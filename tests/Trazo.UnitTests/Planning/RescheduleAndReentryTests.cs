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
/// Coming back after time away: a calendar plan can be moved to today (whole weeks, so every session keeps its weekday), and after
/// a long absence the first session is made lighter on its own (Premium). The fixed clock is Monday 2026-11-16.
/// </summary>
public sealed class RescheduleAndReentryTests() : PlansApiBase(enforced: true)
{
    private static readonly DateOnly StartedLastWeek = new(2026, 11, 9);

    private static object Manual(DateOnly? start, string? mode = null) => new
    {
        name = "Rutina", totalWeeks = 4, startDate = start, schedulingMode = mode, days = mode == "Sequence"
            ? new object[]
            {
                new { title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } },
                new { title = "B", exercises = new object[] { new { exerciseId = "squat", sets = 4, targetRir = 2 } } }
            }
            : new object[]
            {
                new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } },
                new { day = "Thursday", title = "B", exercises = new object[] { new { exerciseId = "squat", sets = 4, targetRir = 2 } } }
            }
    };

    private async Task<HttpClient> StartAsync(string email, bool premium, DateOnly? start, string? mode = null)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: premium));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", Manual(start, mode));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static Task<JsonElement> NextAsync(HttpClient client) =>
        client.GetAsync("/api/v1/plans/current/next").ContinueWith(t => JsonAsync(t.Result)).Unwrap();

    private static async Task<List<JsonElement>> BlockAsync(HttpClient client)
    {
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        return block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, DateTimeOffset at) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed", performedAt = at,
            sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32())
                .Select(n => new { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = 60m, repetitions = 8, rir = 2, completed = true })).ToArray()
        });

    private async Task<List<PlanAdaptation>> AuditAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanAdaptations.AsNoTracking().ToListAsync();
    }

    private async Task MakeLastWorkoutOldAsync(int daysAgo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        foreach (var log in await db.WorkoutLogs.ToListAsync())
        {
            log.PerformedAt = Now.AddDays(-daysAgo);
            log.PerformedOn = DateOnly.FromDateTime(Now.AddDays(-daysAgo).UtcDateTime);
        }
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ reschedule from today

    [Fact]
    public async Task ThePlanOffersToMoveWhenTheProgramIsAWeekBehind_NotBefore()
    {
        var behind = await StartAsync("res-offer@example.com", premium: false, StartedLastWeek);
        Assert.Equal(1, (await NextAsync(behind)).GetProperty("reschedule").GetProperty("weeksBehind").GetInt32());

        var onTime = await StartAsync("res-ontime@example.com", premium: false, start: null);
        Assert.Equal(JsonValueKind.Null, (await NextAsync(onTime)).GetProperty("reschedule").ValueKind);
    }

    [Fact]
    public async Task MovingKeepsTheWeekdays_PutsTheFirstPendingWeekOnTheCurrentOne_AndIsIdempotent()
    {
        var client = await StartAsync("res-move@example.com", premium: false, StartedLastWeek);
        var before = await BlockAsync(client);
        Assert.Equal("2026-11-09", before[0].GetProperty("date").GetString());

        var response = await client.PostAsync("/api/v1/plans/current/reschedule", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await JsonAsync(response);
        Assert.Equal(1, result.GetProperty("weeksShifted").GetInt32());
        Assert.Equal(8, result.GetProperty("sessionsMoved").GetInt32()); // four weeks of two sessions
        Assert.Equal("2026-11-16", result.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-13", result.GetProperty("endDate").GetString());

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(1, week.GetProperty("weekNumber").GetInt32());
        var dates = week.GetProperty("sessions").EnumerateArray().Select(s => s.GetProperty("date").GetString()).ToList();
        Assert.Equal(["2026-11-16", "2026-11-19"], dates); // Monday and Thursday, as before
        var next = await NextAsync(client);
        Assert.Equal(JsonValueKind.Null, next.GetProperty("reschedule").ValueKind);
        Assert.Equal(0, next.GetProperty("overdue").GetArrayLength());

        var again = await JsonAsync(await client.PostAsync("/api/v1/plans/current/reschedule", null));
        Assert.Equal(0, again.GetProperty("weeksShifted").GetInt32());
        Assert.Equal("2026-11-16", again.GetProperty("startDate").GetString());
        var audit = Assert.Single(await AuditAsync(), a => a.Kind == "reschedule");
        Assert.Equal(8, audit.SessionsChanged);
    }

    [Fact]
    public async Task WhatWasAlreadyDoneIsNeverRewritten_AndTheDaysSkippedStayOpenForLateWorkouts()
    {
        var client = await StartAsync("res-done@example.com", premium: false, StartedLastWeek);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddDays(-5))).StatusCode); // Wed 11 Nov

        var result = await JsonAsync(await client.PostAsync("/api/v1/plans/current/reschedule", null));
        Assert.Equal(7, result.GetProperty("sessionsMoved").GetInt32()); // the pending ones
        var after = await BlockAsync(client);
        Assert.Equal("2026-11-09", after[0].GetProperty("date").GetString()); // the one done keeps its planned date
        Assert.Equal("Completed", after[0].GetProperty("status").GetString());
        Assert.Equal("2026-11-19", after[1].GetProperty("date").GetString()); // the pending Thursday of week 1 moved a week

        // A workout of the days that were skipped can still be registered: the past did not close.
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, after[1], Now.AddDays(-4))).StatusCode); // Thu 12 Nov, before the new start
        Assert.Equal(HttpStatusCode.BadRequest, (await LogAsync(client, after[2], Now.AddDays(-8))).StatusCode); // before the plan ever started
    }

    [Fact]
    public async Task ASequencePlanHasNoCalendarToMove_AndTheRouteNeedsASignedInUserWithAPlan()
    {
        var sequence = await StartAsync("res-seq@example.com", premium: false, StartedLastWeek, "Sequence");
        Assert.Equal(HttpStatusCode.BadRequest, (await sequence.PostAsync("/api/v1/plans/current/reschedule", null)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await NextAsync(sequence)).GetProperty("reschedule").ValueKind);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PostAsync("/api/v1/plans/current/reschedule", null)).StatusCode);
        var none = ClientFor(await AddUserAsync("res-none@example.com", subscribed: false));
        Assert.Equal(HttpStatusCode.NotFound, (await none.PostAsync("/api/v1/plans/current/reschedule", null)).StatusCode);
    }

    // ------------------------------------------------------------------ a lighter first session after a long absence

    [Fact]
    public async Task AfterALongAbsenceThePremiumFirstSessionIsLighter_OncePerAbsence()
    {
        var client = await StartAsync("re-premium@example.com", premium: true, StartedLastWeek, "Sequence");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        await MakeLastWorkoutOldAsync(30);
        var original = sessions[1].GetProperty("exercises")[0];
        Assert.Equal(4, original.GetProperty("sets").GetInt32());

        var next = await NextAsync(client);
        var reentry = next.GetProperty("reentry");
        Assert.True(reentry.GetProperty("applied").GetBoolean());
        Assert.Contains("aligerado", reentry.GetProperty("message").GetString());
        var lighter = next.GetProperty("session").GetProperty("exercises")[0];
        Assert.Equal(3, lighter.GetProperty("sets").GetInt32()); // one set less
        Assert.Equal(original.GetProperty("targetRir").GetDecimal() + 1, lighter.GetProperty("targetRir").GetDecimal()); // one more in reserve

        // Reading again changes nothing more: it is once per absence.
        var again = await NextAsync(client);
        Assert.Equal(3, again.GetProperty("session").GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.True(again.GetProperty("reentry").GetProperty("applied").GetBoolean());
        var audit = Assert.Single(await AuditAsync(), a => a.Kind == "reentry");
        Assert.Equal(Now.AddDays(-30), audit.EvidenceAt);
        // The block shows the stored session, and later ones are untouched.
        var block = await BlockAsync(client);
        Assert.Equal(3, block[1].GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.Equal(4, block[2].GetProperty("exercises")[0].GetProperty("sets").GetInt32());
    }

    [Fact]
    public async Task ARescheduleThatLosesTheRaceAnswers409_MovesNothing_AndWorksWhenRepeated()
    {
        var client = await StartAsync("res-race@example.com", premium: false, StartedLastWeek);
        var before = (await BlockAsync(client)).Select(s => s.GetProperty("date").GetString()).ToList();

        RaceOn(q => q);
        var response = await client.PostAsync("/api/v1/plans/current/reschedule", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("plan_modified_concurrently", (await JsonAsync(response)).GetProperty("title").GetString());

        Assert.Equal(before, (await BlockAsync(client)).Select(s => s.GetProperty("date").GetString()).ToList()); // nothing moved
        Assert.DoesNotContain(await AuditAsync(), a => a.Kind == "reschedule");                                  // and nothing was recorded

        var again = await client.PostAsync("/api/v1/plans/current/reschedule", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, (await JsonAsync(again)).GetProperty("weeksShifted").GetInt32());
        Assert.Single(await AuditAsync(), a => a.Kind == "reschedule");
    }

    [Fact]
    public async Task AReentryThatLosesTheRaceLightensNothing_ReportsItAsNotApplied_AndAppliesOnTheNextRead()
    {
        var client = await StartAsync("re-race@example.com", premium: true, StartedLastWeek, "Sequence");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        await MakeLastWorkoutOldAsync(30);

        RaceOn(q => q.Where(s => s.Status == PlannedSessionStatus.Planned));
        var lost = await NextAsync(client); // the read itself never fails: the easing is simply not claimed
        Assert.False(lost.GetProperty("reentry").GetProperty("applied").GetBoolean());
        Assert.Equal(4, lost.GetProperty("session").GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.DoesNotContain(await AuditAsync(), a => a.Kind == "reentry");

        var next = await NextAsync(client);
        Assert.True(next.GetProperty("reentry").GetProperty("applied").GetBoolean());
        Assert.Equal(3, next.GetProperty("session").GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.Single(await AuditAsync(), a => a.Kind == "reentry");
    }

    [Fact]
    public async Task TwoReadsAtOnceLightenTheSessionOnlyOnce()
    {
        var client = await StartAsync("re-race@example.com", premium: true, StartedLastWeek, "Sequence");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        await MakeLastWorkoutOldAsync(30);
        var results = await Task.WhenAll(NextAsync(client), NextAsync(client));
        Assert.All(results, r => Assert.True(r.GetProperty("reentry").GetProperty("applied").GetBoolean()));
        Assert.Equal(3, (await BlockAsync(client))[1].GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.Single(await AuditAsync(), a => a.Kind == "reentry");
    }

    [Fact]
    public async Task WithoutPremiumTheUserIsAdvisedButNothingIsChanged()
    {
        var client = await StartAsync("re-free@example.com", premium: false, StartedLastWeek, "Sequence");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        await MakeLastWorkoutOldAsync(30);
        var next = await NextAsync(client);
        Assert.False(next.GetProperty("reentry").GetProperty("applied").GetBoolean());
        Assert.Contains("30 días", next.GetProperty("reentry").GetProperty("message").GetString());
        Assert.Equal(4, next.GetProperty("session").GetProperty("exercises")[0].GetProperty("sets").GetInt32());
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task ACalendarPlanGetsTheLighterSessionToo_AndNothingHappensBeforeThreeWeeksOrWithoutHistory()
    {
        var client = await StartAsync("re-calendar@example.com", premium: true, StartedLastWeek);
        Assert.Equal(JsonValueKind.Null, (await NextAsync(client)).GetProperty("reentry").ValueKind); // never trained: nothing to come back from
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        await MakeLastWorkoutOldAsync(10);
        Assert.Equal(JsonValueKind.Null, (await NextAsync(client)).GetProperty("reentry").ValueKind); // ten days is not a long absence

        await MakeLastWorkoutOldAsync(25);
        var next = await NextAsync(client);
        Assert.True(next.GetProperty("reentry").GetProperty("applied").GetBoolean());
        Assert.Equal(3, next.GetProperty("session").GetProperty("exercises")[0].GetProperty("sets").GetInt32());
    }
}
