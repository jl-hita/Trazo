using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Health data is only stored with the consent. Interactive choices are refused; a workout is never refused: it is kept without what
/// it says about health, and nothing is adapted from it.
/// </summary>
public sealed class HealthConsentEnforcementTests() : PlansApiBase(enforced: false)
{
    private async Task<(Guid UserId, HttpClient Client, JsonElement Week)> StartWithoutConsentAsync()
    {
        var user = await AddUserAsync("ana@example.com", healthConsent: false);
        var client = ClientFor(user);
        (await client.PostAsJsonAsync("/api/v1/plans", Profile())).EnsureSuccessStatusCode();
        return (user.Id, client, await JsonAsync(await client.GetAsync("/api/v1/plans/current/week")));
    }

    private static string LogUrl(JsonElement session) => $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log";

    private static object PainfulLog(JsonElement session, Guid? id = null)
    {
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;
        return new
        {
            outcome = "Partial", nonCompletionReason = "Illness", painReported = true, clientRequestId = id,
            sets = new object[]
            {
                new { exerciseId = exercise, setNumber = 1, weightKg = 60m, repetitions = 8, rir = 2, completed = true, painReported = true },
                new { exerciseId = exercise, setNumber = 2, completed = false, skippedReason = "pain" },
                new { exerciseId = exercise, setNumber = 3, completed = false, skippedReason = "illness" }
            },
            exerciseFeedback = new Dictionary<string, object> { [exercise] = new { stimulus = "adequate", discomfort = "strong" } }
        };
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("health_consent_required", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task APlanWithInjuryAreas_NeedsTheConsent_AndOneWithoutDoesNot()
    {
        var client = ClientFor(await AddUserAsync("ana@example.com", healthConsent: false));

        await AssertRefusedAsync(await client.PostAsJsonAsync("/api/v1/plans", Profile(limitations: new[] { "Knee" })));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/plans/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
    }

    [Fact]
    public async Task AddingAnInjuryArea_NeedsTheConsent()
    {
        var (_, client, _) = await StartWithoutConsentAsync();

        await AssertRefusedAsync(await client.PutAsJsonAsync("/api/v1/plans/current/limitations", new { limitations = new[] { "Knee" } }));
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/v1/plans/current/limitations", new { limitations = Array.Empty<string>() })).StatusCode);
    }

    [Fact]
    public async Task OmittingASessionForIllness_NeedsTheConsent()
    {
        var (_, client, week) = await StartWithoutConsentAsync();
        var url = $"/api/v1/plans/current/sessions/{week.GetProperty("sessions")[0].GetProperty("id").GetString()}/skip";

        await AssertRefusedAsync(await client.PostAsJsonAsync(url, new { reason = "Illness" }));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(url, new { reason = "Time" })).StatusCode);
    }

    [Fact]
    public async Task AWorkoutIsKept_WithoutAnythingAboutHealth_AndNothingIsAdaptedFromIt()
    {
        var (userId, client, week) = await StartWithoutConsentAsync();
        var session = week.GetProperty("sessions")[0];

        var response = await client.PostAsJsonAsync(LogUrl(session), PainfulLog(session));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var adaptation = (await JsonAsync(response)).GetProperty("adaptation");
        Assert.Contains("consentimiento", Assert.Single(adaptation.GetProperty("notes").EnumerateArray()).GetString());
        Assert.Empty(adaptation.GetProperty("applied").EnumerateArray());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var log = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).SingleAsync(l => l.UserId == userId);
        Assert.False(log.PainReported);
        Assert.Equal("Other", log.NonCompletionReason);
        Assert.All(log.Sets, s => Assert.False(s.PainReported));
        Assert.Equal(["other", "other"], log.Sets.Where(s => !s.Completed).Select(s => s.SkippedReason!).ToArray());
        Assert.Equal(60m, log.Sets.Single(s => s.Completed).WeightKg);
        Assert.DoesNotContain("strong", log.ExerciseFeedbackJson);
        Assert.Contains("adequate", log.ExerciseFeedbackJson);
        Assert.Empty(await db.PlanAdaptations.AsNoTracking().Where(a => a.WorkoutLogId == log.Id).ToListAsync());
    }

    [Fact]
    public async Task SendingTheSameWorkoutAgain_IsStillTheSameWorkout()
    {
        var (_, client, week) = await StartWithoutConsentAsync();
        var session = week.GetProperty("sessions")[0];
        var body = PainfulLog(session, Guid.NewGuid());

        (await client.PostAsJsonAsync(LogUrl(session), body)).EnsureSuccessStatusCode();
        var again = await client.PostAsJsonAsync(LogUrl(session), body);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True((await JsonAsync(again)).GetProperty("replayed").GetBoolean());
    }

    [Fact]
    public async Task AWorkoutWithNothingAboutHealth_IsAdaptedAsAlways()
    {
        var (_, client, week) = await StartWithoutConsentAsync();
        var session = week.GetProperty("sessions")[0];

        var response = await client.PostAsJsonAsync(LogUrl(session), new { outcome = "Completed", sets = CompleteSets(session) });

        var adaptation = (await JsonAsync(response)).GetProperty("adaptation");
        Assert.NotEmpty(adaptation.GetProperty("exercises").EnumerateArray());
        Assert.DoesNotContain(adaptation.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("consentimiento"));
    }

    [Fact]
    public async Task OnceTheConsentIsGiven_TheDataIsStored()
    {
        var (userId, client, week) = await StartWithoutConsentAsync();
        (await client.PutAsJsonAsync("/api/v1/account/health-consent", new { consent = true })).EnsureSuccessStatusCode();
        var session = week.GetProperty("sessions")[0];

        (await client.PostAsJsonAsync(LogUrl(session), PainfulLog(session))).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.True((await db.WorkoutLogs.AsNoTracking().SingleAsync(l => l.UserId == userId)).PainReported);
    }
}
