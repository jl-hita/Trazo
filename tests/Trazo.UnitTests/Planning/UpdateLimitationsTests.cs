using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Trazo.Application.Planning;
using Trazo.Domain.Billing;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>An injury outside a session: the user replaces the body areas they cannot load and the pending sessions adapt.</summary>
public sealed class UpdateLimitationsApiTests() : PlansApiBase(enforced: false)
{
    private const string Url = "/api/v1/plans/current/limitations";

    private static IEnumerable<string> ExerciseIds(JsonElement week) => week.GetProperty("sessions").EnumerateArray()
        .SelectMany(s => s.GetProperty("exercises").EnumerateArray()).Select(e => e.GetProperty("exerciseId").GetString()!);

    /// <summary>An area whose rules rule out something the first block actually prescribes, so the test proves a swap happened.</summary>
    private static string AreaThatHitsThePlan(JsonElement week)
    {
        var catalog = PlanTestCatalog.Candidates();
        var prescribed = ExerciseIds(week).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return LimitationRules.Areas.First(area => LimitationRules.ExcludedExerciseIds([area], catalog).Any(prescribed.Contains));
    }

    [Fact]
    public async Task AnInjuryReplacesWhatItRulesOutInPendingSessions_AndIsRecorded()
    {
        var (_, client, week) = await StartPlanAsync();
        var area = AreaThatHitsThePlan(week);
        var ruledOut = LimitationRules.ExcludedExerciseIds([area], PlanTestCatalog.Candidates()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var response = await client.PutAsJsonAsync(Url, new { limitations = new[] { area.ToLowerInvariant() } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(area, (await JsonAsync(response)).GetProperty("limitations")[0].GetString());

        var after = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.DoesNotContain(ExerciseIds(after), ruledOut.Contains);
        // The adaptation history lists only the engine's changes; the user's own decision is kept in the audit trail.
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanAdaptations.SingleAsync(a => a.Kind == "user_limitations");
        Assert.Equal(PrescriptionSources.User, row.Source);
        Assert.True(row.SessionsChanged > 0);
    }

    [Fact]
    public async Task RecoveringClearsTheAreas()
    {
        var (_, client, week) = await StartPlanAsync();
        var area = AreaThatHitsThePlan(week);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Url, new { limitations = new[] { area } })).StatusCode);
        var cleared = await client.PutAsJsonAsync(Url, new { limitations = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await JsonAsync(cleared)).GetProperty("limitations").EnumerateArray());
    }

    [Fact]
    public async Task AnUnknownAreaChangesNothing()
    {
        var (_, client, _) = await StartPlanAsync(profile: Profile(limitations: new[] { "Knee" }));
        var response = await client.PutAsJsonAsync(Url, new { limitations = new[] { "Nonsense" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal("Knee", plan.GetProperty("limitations")[0].GetString());
    }

    [Fact]
    public async Task WithoutAnActivePlanTheAnswerIsNotFound()
    {
        var client = ClientFor(await AddUserAsync("none@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Url, new { limitations = new[] { "Knee" } })).StatusCode);
    }

    [Fact]
    public async Task AnonymousCallersAreRejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PutAsJsonAsync(Url, new { limitations = new[] { "Knee" } })).StatusCode);
    }
}

public sealed class UpdateLimitationsPremiumTests() : PlansApiBase(enforced: true)
{
    [Fact]
    public async Task WithoutPremiumTheAdaptationIsRefused()
    {
        var user = await AddUserAsync("expired-injury@example.com", subscribed: true);
        var client = ClientFor(user);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(x => x.UserId == user.Id);
            subscription.Status = SubscriptionStatus.Expired;
            subscription.ExpiresAt = Now.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/limitations", new { limitations = new[] { "Knee" } });
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("premium_required", (await JsonAsync(response)).GetProperty("title").GetString());
    }
}
