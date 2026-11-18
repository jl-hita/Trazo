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
/// Sessions not done: a late session stays pending and the queue shifts (nothing lost, nothing duplicated, order kept), omitting is a
/// deliberate act that is not training and not an exposure, and the longer the absence the more the return is eased. The fixed clock is
/// Monday 2026-11-16.
/// </summary>
public sealed class SessionsNotDoneTests() : PlansApiBase(enforced: true)
{
    private static object ManualSequence() => new
    {
        name = "Rutina", totalWeeks = 4, schedulingMode = "Sequence",
        days = new object[]
        {
            new { title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } },
            new { title = "B", exercises = new object[] { new { exerciseId = "squat", sets = 4, targetRir = 2 } } }
        }
    };

    private async Task<HttpClient> StartAsync(string email, bool premium)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: premium));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", ManualSequence());
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

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed", performedAt = (DateTimeOffset?)null,
            sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32())
                .Select(n => new { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = 60m, repetitions = 8, rir = 2, completed = true })).ToArray()
        });

    private static Task<HttpResponseMessage> SkipAsync(HttpClient client, JsonElement session, string? reason) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/skip", new { reason });

    private async Task<List<PlanAdaptation>> AuditAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanAdaptations.AsNoTracking().ToListAsync();
    }

    private async Task MakeWorkoutsOldAsync(int daysAgo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        foreach (var log in await db.WorkoutLogs.Where(l => l.Outcome != WorkoutOutcome.NotCompleted).ToListAsync())
        {
            log.PerformedAt = Now.AddDays(-daysAgo);
            log.PerformedOn = DateOnly.FromDateTime(Now.AddDays(-daysAgo).UtcDateTime);
        }
        await db.SaveChangesAsync();
    }

    private static string Id(JsonElement session) => session.GetProperty("id").GetString()!;

    // ------------------------------------------------------------------ the queue shifts

    [Fact]
    public async Task ALateSessionKeepsTheQueueIntact_NothingLostNothingDuplicatedAndTheOrderUnchanged()
    {
        var client = await StartAsync("shift-queue@example.com", premium: false);
        var before = await BlockAsync(client);
        var order = before.Select(Id).ToList();
        var titles = before.Select(s => s.GetProperty("title").GetString()).ToList();

        // The user does not train: reading the plan any number of times changes nothing and the next session stays the first one.
        for (var i = 0; i < 3; i++)
            Assert.Equal(order[0], Id((await NextAsync(client)).GetProperty("session")));

        // Session 1 is done, session 2 is not: the next one is still 2, whatever the days that passed.
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, before[0])).StatusCode);
        Assert.Equal(order[1], Id((await NextAsync(client)).GetProperty("session")));

        var after = await BlockAsync(client);
        Assert.Equal(order, after.Select(Id).ToList());                                           // same sessions, same order
        Assert.Equal(titles, after.Select(s => s.GetProperty("title").GetString()).ToList());
        Assert.Equal(after.Count, after.Select(s => s.GetProperty("sequenceIndex").GetInt32()).Distinct().Count()); // no duplicates
        Assert.Equal(Enumerable.Range(1, after.Count), after.Select(s => s.GetProperty("sequenceIndex").GetInt32()));   // no gaps
        Assert.Equal(before.Select(s => s.GetProperty("exercises").GetRawText()), after.Select(s => s.GetProperty("exercises").GetRawText())); // prescription untouched
        Assert.Empty(await AuditAsync());
    }

    // ------------------------------------------------------------------ omitting

    [Fact]
    public async Task OmittingResolvesTheSessionAsSkipped_GoesOnWithTheNextOne_AndTouchesNothingElse()
    {
        var client = await StartAsync("skip-basic@example.com", premium: true);
        var before = await BlockAsync(client);

        var response = await SkipAsync(client, before[0], "Time");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await JsonAsync(response);
        Assert.Equal("Skipped", result.GetProperty("session").GetProperty("status").GetString());
        Assert.False(result.GetProperty("adapted").GetBoolean());

        Assert.Equal(Id(before[1]), Id((await NextAsync(client)).GetProperty("session")));
        var after = await BlockAsync(client);
        Assert.Equal(before.Select(Id), after.Select(Id));
        Assert.Equal(before.Skip(1).Select(s => s.GetProperty("exercises").GetRawText()), after.Skip(1).Select(s => s.GetProperty("exercises").GetRawText()));

        var audit = Assert.Single(await AuditAsync()); // the decision is recorded, and it is the only thing
        Assert.Equal("skip", audit.Kind);
        Assert.Contains("Time", audit.Explanation);
        Assert.NotNull(audit.WorkoutLogId);
    }

    [Fact]
    public async Task OmittingTwiceIsRefused_AnInvalidReasonIsRefused_AndAnOmittedSessionCannotBeLoggedAfterwards()
    {
        var client = await StartAsync("skip-twice@example.com", premium: false);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, sessions[0], null)).StatusCode); // no reason = Other
        Assert.Equal(HttpStatusCode.Conflict, (await SkipAsync(client, sessions[0], "Time")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SkipAsync(client, sessions[1], "boredom")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SkipAsync(client, sessions[1], "None")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{Guid.NewGuid()}/skip", new { reason = "Time" })).StatusCode);
    }

    [Fact]
    public async Task AnOmittedSessionIsNotTraining_SoItDoesNotRestartTheAbsence()
    {
        var client = await StartAsync("skip-clock@example.com", premium: false);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        await MakeWorkoutsOldAsync(30);
        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, sessions[1], "Motivation")).StatusCode); // today

        var next = await NextAsync(client);
        Assert.Equal(30, next.GetProperty("reentry").GetProperty("daysSinceLastWorkout").GetInt32());
        Assert.Equal("AbsenceShort", next.GetProperty("adjustment").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task APhysicalReasonGoesThroughTheAdaptationOnlyWithPremium()
    {
        var free = await StartAsync("skip-ill-free@example.com", premium: false);
        var result = await JsonAsync(await SkipAsync(free, (await BlockAsync(free))[0], "Illness"));
        Assert.False(result.GetProperty("adapted").GetBoolean());

        var premium = await StartAsync("skip-ill-premium@example.com", premium: true);
        var before = await BlockAsync(premium);
        var ok = await SkipAsync(premium, before[0], "Illness");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Skipped", (await JsonAsync(ok)).GetProperty("session").GetProperty("status").GetString());
    }

    [Fact]
    public async Task RepeatedOmissionsSuggestRebuilding_ButOneOffsDoNot()
    {
        var client = await StartAsync("skip-pattern@example.com", premium: false);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, sessions[1], "Time")).StatusCode);
        Assert.Equal("OnTrack", (await NextAsync(client)).GetProperty("adjustment").GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, sessions[2], "Time")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SkipAsync(client, sessions[3], "Motivation")).StatusCode);
        var adjustment = (await NextAsync(client)).GetProperty("adjustment");
        Assert.Equal("Rebuild", adjustment.GetProperty("strategy").GetString());
        Assert.Equal("RepeatedSkips", adjustment.GetProperty("reason").GetString());
        Assert.True(adjustment.GetProperty("canSkip").GetBoolean());
    }

    // ------------------------------------------------------------------ the longer the absence, the gentler the return

    private static int SetsOf(JsonElement session) => session.GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32());

    [Theory]
    [InlineData(25, 1, "EaseBack", "AbsenceShort")]
    [InlineData(50, 2, "EaseBack", "AbsenceLong")]
    [InlineData(100, 2, "Rebuild", "AbsenceVeryLong")]
    public async Task TheReturnIsEasedBySessionsAccordingToTheAbsence_OnceAndOnlyWithPremium(int days, int eased, string strategy, string reason)
    {
        var client = await StartAsync($"ease-{days}@example.com", premium: true);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        await MakeWorkoutsOldAsync(days);
        var original = (await BlockAsync(client)).Select(SetsOf).ToList();

        var next = await NextAsync(client);
        Assert.Equal(strategy, next.GetProperty("adjustment").GetProperty("strategy").GetString());
        Assert.Equal(reason, next.GetProperty("adjustment").GetProperty("reason").GetString());
        Assert.True(next.GetProperty("reentry").GetProperty("applied").GetBoolean());

        var easedNow = (await BlockAsync(client)).Select(SetsOf).ToList();
        for (var i = 0; i < easedNow.Count; i++)
        {
            var isEased = i >= 1 && i < 1 + eased; // session 1 is done; the pending ones start at index 1
            Assert.Equal(isEased ? original[i] - 1 : original[i], easedNow[i]); // one set less in its only exercise
        }

        // Reading again changes nothing: once per absence.
        await NextAsync(client);
        await NextAsync(client);
        Assert.Equal(easedNow, (await BlockAsync(client)).Select(SetsOf).ToList());
        var audit = Assert.Single(await AuditAsync(), a => a.Kind == "reentry");
        Assert.Equal(eased, audit.SessionsChanged);
    }

    [Fact]
    public async Task WithoutPremiumTheReturnIsOnlyAdvisedNeverRewritten()
    {
        var client = await StartAsync("ease-free@example.com", premium: false);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        await MakeWorkoutsOldAsync(60);
        var original = (await BlockAsync(client)).Select(s => s.GetProperty("exercises").GetRawText()).ToList();

        var next = await NextAsync(client);
        Assert.False(next.GetProperty("reentry").GetProperty("applied").GetBoolean());
        Assert.Equal(2, next.GetProperty("adjustment").GetProperty("easeBackSessions").GetInt32());
        Assert.Equal(original, (await BlockAsync(client)).Select(s => s.GetProperty("exercises").GetRawText()).ToList());
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task TheExplanationsAreInTheLanguageOfTheRequest()
    {
        var client = await StartAsync("not-done-en@example.com", premium: false);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        await MakeWorkoutsOldAsync(100);

        var message = (await NextAsync(client)).GetProperty("adjustment").GetProperty("message").GetString()!;
        Assert.Contains("100 days without training", message);
        Assert.DoesNotContain("sesión", message);
        var skipped = await JsonAsync(await SkipAsync(client, sessions[1], "Other"));
        Assert.StartsWith("Session skipped.", skipped.GetProperty("message").GetString());
        var refused = await SkipAsync(client, sessions[1], "Time");
        Assert.DoesNotContain("resuelta", await refused.Content.ReadAsStringAsync());
    }
}
