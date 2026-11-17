using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class AsynchronousSplitRulesTests
{
    private static PlanProfileInput Input(string? mode = "Sequence", int? perPeriod = 3, string[]? days = null) => new(
        "Hypertrophy", "Intermediate", null, 12, days, 75, [], [], [], null, true, null, null, mode, perPeriod);

    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void ASequencePlanNeedsTheSessionsOfOneRotation_AndHasNoDays()
    {
        var ok = PlanProfileParser.Parse(Input(), Today);
        Assert.True(ok.Succeeded);
        Assert.Equal(PlanSchedulingMode.Sequence, ok.Profile!.SchedulingMode);
        Assert.Equal(3, ok.Profile.SessionsPerPeriod);
        Assert.Equal(3, ok.Profile.TrainingDays.Count); // synthetic days only lay the rotation out in the engine

        Assert.Contains("sessionsPerPeriod", PlanProfileParser.Parse(Input(perPeriod: null), Today).Errors.Keys);
        Assert.Contains("sessionsPerPeriod", PlanProfileParser.Parse(Input(perPeriod: 7), Today).Errors.Keys);
        Assert.Contains("sessionsPerPeriod", PlanProfileParser.Parse(Input(perPeriod: 1), Today).Errors.Keys);
        Assert.Contains("trainingDays", PlanProfileParser.Parse(Input(days: ["Monday", "Friday"]), Today).Errors.Keys);
        Assert.Contains("schedulingMode", PlanProfileParser.Parse(Input(mode: "Weekly"), Today).Errors.Keys);
    }

    [Fact]
    public void ACalendarPlanIsUnchanged_AndTheDefaultModeIsCalendar()
    {
        var parsed = PlanProfileParser.Parse(Input(mode: null, perPeriod: null, days: ["Monday", "Wednesday", "Friday"]), Today);
        Assert.True(parsed.Succeeded);
        Assert.Equal(PlanSchedulingMode.Calendar, parsed.Profile!.SchedulingMode);
        Assert.Equal(3, parsed.Profile.SessionsPerPeriod);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], parsed.Profile.TrainingDays);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TheSyntheticDaysAreDistinctAndAsManyAsTheSessions(int sessions)
    {
        var days = PlanProfileParser.SyntheticDays(sessions);
        Assert.Equal(sessions, days.Distinct().Count());
    }

    [Fact]
    public void TheStructureOfTheMacrocycleIsTheSameFromPeriodsAsFromDates()
    {
        var start = new DateOnly(2026, 11, 9);
        var phases = new[] { new PhaseSpan(1, "accumulation", start, start.AddDays(27)), new PhaseSpan(2, "deload", start.AddDays(28), start.AddDays(34)) };
        var fromDates = PlanSchedule.Slots(start, phases);
        var fromPeriods = PlanSchedule.Slots([new PeriodSpan(1, "accumulation", 1, 4), new PeriodSpan(2, "deload", 5, 5)]);
        Assert.Equal(fromDates, fromPeriods);
    }

    [Fact]
    public void TheSequenceTimelineHasNoLateSessions_AndFollowsTheRotation()
    {
        var past = new DateOnly(2026, 1, 1);
        var pending = new[] { new TimelineSession(Guid.NewGuid(), 4, 2, past, "B"), new TimelineSession(Guid.NewGuid(), 3, 2, null, "A") };
        var result = new SequenceTimeline().Next(pending, Today, 3);
        Assert.Equal(3, result.Next!.SequenceIndex);
        Assert.Empty(result.Overdue);
        Assert.Equal("Sequence", new SequenceTimeline().Mode);
    }
}

/// <summary>
/// The asynchronous split: the program is a rotation of sessions, not a calendar. The clock never moves in these tests (it is fixed
/// at Monday 2026-11-16, which a calendar plan started on the 9th would call week 2); the program advances only when sessions are
/// resolved, which is exactly the behaviour that distinguishes it.
/// </summary>
public sealed class AsynchronousSplitApiTests() : PlansApiBase(enforced: true)
{
    private static readonly DateOnly Started = new(2026, 11, 9);

    private static object GeneratedSequence(int perPeriod = 2, object? trainingDays = null) => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12, startDate = Started, schedulingMode = "Sequence",
        sessionsPerPeriod = perPeriod, trainingDays, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>()
    };

    private static object ManualSequence(int totalWeeks = 4) => new
    {
        name = "Rotación", totalWeeks, startDate = Started, schedulingMode = "Sequence", days = new object[]
        {
            new { title = "Empuje A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4 } } },
            new { title = "Empuje B", exercises = new object[] { new { exerciseId = "bench-press", sets = 4 } } },
            new { title = "Pierna", exercises = new object[] { new { exerciseId = "squat", sets = 4 } } }
        }
    };

    private async Task<HttpClient> StartGeneratedAsync(string email, int perPeriod = 2)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: true));
        var created = await client.PostAsJsonAsync("/api/v1/plans", GeneratedSequence(perPeriod));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private async Task<HttpClient> StartManualAsync(string email, bool premium = false, int totalWeeks = 4)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: premium));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", ManualSequence(totalWeeks));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static Task<JsonElement> NextAsync(HttpClient client) => client.GetAsync("/api/v1/plans/current/next").ContinueWith(t => JsonAsync(t.Result)).Unwrap();

    private static async Task<List<JsonElement>> BlockAsync(HttpClient client)
    {
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        return block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, DateTimeOffset? performedAt = null, int rir = 2, bool stressed = false)
    {
        var sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, stressed ? 1 : e.GetProperty("sets").GetInt32())
            .Select(n => new
            {
                exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = stressed ? 50m : 60m, repetitions = stressed ? 6 : 8,
                rir = stressed ? 0 : rir, completed = true
            })).ToArray();
        object body = stressed
            ? new { outcome = "Partial", nonCompletionReason = "Fatigue", recovery = "Insufficient", sessionRpe = 10, sets, performedAt }
            : new { outcome = "Completed", sets, performedAt };
        return client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", body);
    }

    // ------------------------------------------------------------------ creation

    [Fact]
    public async Task ASequencePlanIsCreatedWithoutDaysOrDates_AndTheOptionsAnnounceTheModes()
    {
        var client = ClientFor(await AddUserAsync("seq-create@example.com", subscribed: true));
        var options = await JsonAsync(await client.GetAsync("/api/v1/plans/options"));
        Assert.Equal(["Calendar", "Sequence"], options.GetProperty("schedulingModes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(2, options.GetProperty("minimumSessionsPerPeriod").GetInt32());
        Assert.Equal(6, options.GetProperty("maximumSessionsPerPeriod").GetInt32());

        var created = await client.PostAsJsonAsync("/api/v1/plans", GeneratedSequence(3));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await JsonAsync(created);
        Assert.Equal("Sequence", plan.GetProperty("schedulingMode").GetString());
        Assert.Equal(3, plan.GetProperty("sessionsPerPeriod").GetInt32());
        Assert.Empty(plan.GetProperty("trainingDays").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("endDate").ValueKind);
        var phases = plan.GetProperty("macrocycle").GetProperty("phases").EnumerateArray().ToList();
        Assert.All(phases, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("startDate").ValueKind));
        Assert.Equal(1, phases[0].GetProperty("startPeriod").GetInt32());
        Assert.Contains(phases, p => p.GetProperty("type").GetString() == "deload" && p.GetProperty("startPeriod").GetInt32() == 5);
    }

    [Fact]
    public async Task ASequencePlanRejectsFixedDays_AndNeedsTheSessionsPerCycle()
    {
        var client = ClientFor(await AddUserAsync("seq-invalid@example.com", subscribed: true));
        var withDays = await client.PostAsJsonAsync("/api/v1/plans", GeneratedSequence(3, new[] { "Monday", "Friday" }));
        Assert.Equal(HttpStatusCode.BadRequest, withDays.StatusCode);
        var without = await client.PostAsJsonAsync("/api/v1/plans", new { goal = "Hypertrophy", experience = "Intermediate", schedulingMode = "Sequence" });
        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
    }

    [Fact]
    public async Task TheSessionsHaveNoDate_OnlyAPlaceInTheProgram()
    {
        var client = await StartGeneratedAsync("seq-nodate@example.com", perPeriod: 3);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(JsonValueKind.Null, week.GetProperty("weekStart").ValueKind);
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, s =>
        {
            Assert.Equal(JsonValueKind.Null, s.GetProperty("date").ValueKind);
            Assert.Equal(JsonValueKind.Null, s.GetProperty("day").ValueKind);
        });
        Assert.Equal([1, 2, 3], sessions.Select(s => s.GetProperty("sequenceIndex").GetInt32()));
        Assert.Equal(12, (await BlockAsync(client)).Count); // four cycles of three sessions
    }

    // ------------------------------------------------------------------ the program follows the sessions, not the clock

    [Fact]
    public async Task TrainingOnVariableDays_AdvancesTheProgramByTheSessionsDone_NotByTheCalendar()
    {
        var client = await StartGeneratedAsync("seq-days@example.com", perPeriod: 2);
        var sessions = await BlockAsync(client);
        // Mon, Wed, Thu, Sat of the first week and Mon of the next: no fixed distribution of days.
        DateTimeOffset[] days = [new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero), new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero),
            new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero), new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), new(2026, 11, 16, 7, 0, 0, TimeSpan.Zero)];
        var expectedPeriod = new[] { 1, 1, 2, 2, 3 }; // two sessions per cycle

        for (var i = 0; i < days.Length; i++)
        {
            var next = await NextAsync(client);
            Assert.Equal(sessions[i].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString());
            Assert.Equal(expectedPeriod[i], next.GetProperty("position").GetProperty("period").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, next.GetProperty("session"), days[i])).StatusCode);
        }
        var after = await NextAsync(client);
        Assert.Equal(sessions[5].GetProperty("id").GetString(), after.GetProperty("session").GetProperty("id").GetString());
        Assert.Equal(3, after.GetProperty("position").GetProperty("period").GetInt32());
        Assert.Equal(2, after.GetProperty("position").GetProperty("sessionInPeriod").GetInt32());
        Assert.Equal(0, after.GetProperty("overdue").GetArrayLength()); // nothing is ever late
    }

    [Fact]
    public async Task WithoutTrainingTheProgramDoesNotMove_EvenIfTheCalendarWould()
    {
        var client = await StartGeneratedAsync("seq-still@example.com", perPeriod: 2);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(1, week.GetProperty("weekNumber").GetInt32()); // a calendar plan started a week ago would be in week 2
        Assert.Equal(1, week.GetProperty("mesocycle").GetProperty("sequence").GetInt32());
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal(1, plan.GetProperty("currentWeek").GetInt32());
        Assert.Equal("InProgress", plan.GetProperty("timeline").GetString());
    }

    [Fact]
    public async Task TheDeloadComesWhenTheProgramReachesIt_NotWhenFourWeeksHavePassed()
    {
        var client = await StartGeneratedAsync("seq-deload@example.com", perPeriod: 2);
        var sessions = await BlockAsync(client);
        Assert.Equal(8, sessions.Count);

        // Seven of the eight sessions of the first block: the program is still in block 1, no deload, whatever the time passed.
        for (var i = 0; i < 7; i++)
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[i], Now.AddHours(-9 + i))).StatusCode);
        var almost = await NextAsync(client);
        Assert.Equal(4, almost.GetProperty("position").GetProperty("period").GetInt32());
        Assert.False(almost.GetProperty("position").GetProperty("mesocycle").GetProperty("isDeload").GetBoolean());

        // The eighth resolves the block: the next one is generated and it is the deload (cycle 5 of a 12-cycle plan).
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[7], Now.AddHours(-1))).StatusCode);
        var deload = await NextAsync(client);
        var position = deload.GetProperty("position");
        Assert.Equal(5, position.GetProperty("period").GetInt32());
        Assert.True(position.GetProperty("mesocycle").GetProperty("isDeload").GetBoolean());
        Assert.Equal(2, position.GetProperty("mesocycle").GetProperty("sequence").GetInt32());
        Assert.Equal(9, deload.GetProperty("session").GetProperty("sequenceIndex").GetInt32()); // the numbering continues
    }

    [Fact]
    public async Task ASequenceManualPlanCanBeEdited_KeepingWhatWasLogged_AndNumberingTheRestInOrder()
    {
        var client = await StartManualAsync("seq-edit@example.com", totalWeeks: 4);
        var first = (await NextAsync(client)).GetProperty("session");
        Assert.Equal("Empuje A", first.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, first)).StatusCode);

        var edited = new
        {
            name = "Rotación 2", schedulingMode = "Sequence", days = new object[]
            {
                new { title = "Tirón", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } },
                new { title = "Pierna nueva", exercises = new object[] { new { exerciseId = "squat", sets = 2 } } }
            }
        };
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/manual", edited);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("Rotación 2", (await JsonAsync(response)).GetProperty("macrocycle").GetProperty("name").GetString());

        var sessions = await BlockAsync(client);
        // The logged session keeps its routine and place; the rest of its period and the later ones follow the new routine.
        Assert.Equal("Empuje A", sessions[0].GetProperty("title").GetString());
        Assert.Equal(8, sessions.Count); // 4 periods of 2 sessions
        Assert.Equal("Pierna nueva", sessions[1].GetProperty("title").GetString()); // position 2 of the new routine fills what is left of period 1
        Assert.Equal(Enumerable.Range(1, sessions.Count), sessions.Select(s => s.GetProperty("sequenceIndex").GetInt32()));
        Assert.All(sessions, s => Assert.Equal(JsonValueKind.Null, s.GetProperty("date").ValueKind));

        // What is next now is the first pending session of the edited program, and it can be logged.
        var next = (await NextAsync(client)).GetProperty("session");
        Assert.Equal(2, next.GetProperty("sequenceIndex").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, next)).StatusCode);
    }

    [Fact]
    public async Task ASkippedSessionCountsAsResolved_SoOmittingNeverBlocksTheProgram()
    {
        var client = await StartGeneratedAsync("seq-skip@example.com", perPeriod: 2);
        var sessions = await BlockAsync(client);
        var skipped = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessions[0].GetProperty("id").GetString()}/log",
            new { outcome = "NotCompleted", nonCompletionReason = "Time", sets = Array.Empty<object>(), performedAt = Now.AddDays(-2) });
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        Assert.Equal(sessions[1].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());
    }

    [Fact]
    public async Task OutOfOrderAndLateRegistrationKeepTheProgramOrder_AndDoNotAdaptWithOlderData()
    {
        var client = await StartGeneratedAsync("seq-late@example.com", perPeriod: 3);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[2], Now.AddHours(-2))).StatusCode); // third first
        Assert.Equal(sessions[0].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());

        var late = await LogAsync(client, sessions[0], Now.AddDays(-6), stressed: true); // registered late, happened earlier
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Empty((await JsonAsync(late)).GetProperty("adaptation").GetProperty("applied").EnumerateArray());
        Assert.Equal(sessions[1].GetProperty("id").GetString(), (await NextAsync(client)).GetProperty("session").GetProperty("id").GetString());
    }

    // ------------------------------------------------------------------ recovery drives the advice, not the clock

    [Fact]
    public async Task RecoveryAdviceAppliesToTheSequence_TheSameMusclesTwiceInARowMeanWaitAndAnAlternative()
    {
        var client = await StartManualAsync("seq-recovery@example.com", premium: true);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-5), rir: 0)).StatusCode);

        var next = await NextAsync(client);
        Assert.Equal("Sequence", next.GetProperty("schedulingMode").GetString());
        Assert.Equal(sessions[1].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString());
        var availability = next.GetProperty("availability");
        Assert.Equal("Wait", availability.GetProperty("state").GetString());
        Assert.Equal(sessions[2].GetProperty("id").GetString(),
            Assert.Single(availability.GetProperty("alternatives").EnumerateArray()).GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task WorkingTheLegsFirst_LeavesThePressingSessionReady()
    {
        var client = await StartManualAsync("seq-ready@example.com", premium: true);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[2], Now.AddHours(-3), rir: 0)).StatusCode); // the leg session
        Assert.Equal("Ready", (await NextAsync(client)).GetProperty("availability").GetProperty("state").GetString());
    }

    // ------------------------------------------------------------------ long absences

    [Fact]
    public async Task AfterAWhileWithoutTrainingThePlanStaysWhereItWas_AndSuggestsEasingBackIn()
    {
        var client = await StartGeneratedAsync("seq-away@example.com", perPeriod: 2);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddHours(-1))).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await NextAsync(client)).GetProperty("reentry").ValueKind); // recent workout: nothing to say

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var log = await db.WorkoutLogs.SingleAsync();
            log.PerformedAt = Now.AddDays(-30);
            log.PerformedOn = new DateOnly(2026, 10, 17);
            await db.SaveChangesAsync();
        }
        var next = await NextAsync(client);
        Assert.Equal(sessions[1].GetProperty("id").GetString(), next.GetProperty("session").GetProperty("id").GetString()); // same place
        Assert.Equal(1, next.GetProperty("position").GetProperty("period").GetInt32());
        var reentry = next.GetProperty("reentry");
        Assert.Equal(30, reentry.GetProperty("daysSinceLastWorkout").GetInt32());
        Assert.Contains("30 días", reentry.GetProperty("message").GetString());
    }

    // ------------------------------------------------------------------ manual rotation (free)

    [Fact]
    public async Task AManualSequencePlanIsFree_RepeatsTheRoutinesInOrder_AndNeedsNoDays()
    {
        var client = await StartManualAsync("seq-manual@example.com");
        var sessions = await BlockAsync(client);
        Assert.Equal(12, sessions.Count); // four cycles of three routines
        Assert.Equal(Enumerable.Range(1, 12), sessions.Select(s => s.GetProperty("sequenceIndex").GetInt32()));
        Assert.Equal(["Empuje A", "Empuje B", "Pierna", "Empuje A"], sessions.Take(4).Select(s => s.GetProperty("title").GetString()));
        Assert.All(sessions, s => Assert.Equal(JsonValueKind.Null, s.GetProperty("date").ValueKind));
        Assert.Equal("Sequence", (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("schedulingMode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0], Now.AddDays(-1))).StatusCode);
    }

    [Fact]
    public async Task TheManualSequencePlanFinishesWhenItsLastSessionIsResolved()
    {
        var client = await StartManualAsync("seq-finish@example.com", totalWeeks: 1);
        var sessions = await BlockAsync(client);
        Assert.Equal(3, sessions.Count);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[i], Now.AddHours(-4 + i))).StatusCode);
        var next = await NextAsync(client);
        Assert.Equal(JsonValueKind.Null, next.GetProperty("session").ValueKind);
        Assert.Equal("Finished", next.GetProperty("timeline").GetString());
    }

    // ------------------------------------------------------------------ the same adaptation, with a window in sessions

    [Fact]
    public async Task TheReactiveDeloadStillWorks_AndItsWindowIsTheNextRotationOfSessions()
    {
        var client = await StartManualAsync("seq-adapt@example.com", premium: true);
        var sessions = await BlockAsync(client); // three routines per cycle
        JsonElement last = default;
        for (var i = 0; i < 3; i++)
            last = await JsonAsync(await LogAsync(client, sessions[i], Now.AddHours(-10 + i), stressed: true));
        var deload = Assert.Single(last.GetProperty("adaptation").GetProperty("applied").EnumerateArray(), a => a.GetProperty("kind").GetString() == "deload");
        Assert.Equal(3, deload.GetProperty("sessionsChanged").GetInt32()); // the next cycle, not seven calendar days

        var after = await BlockAsync(client);
        Assert.All(after.Skip(3).Take(3), s => Assert.All(s.GetProperty("exercises").EnumerateArray(), e => Assert.Equal("None", e.GetProperty("progressionRule").GetString())));
        Assert.All(after.Skip(6), s => Assert.NotEqual("None", s.GetProperty("exercises")[0].GetProperty("progressionRule").GetString()));
    }
}
