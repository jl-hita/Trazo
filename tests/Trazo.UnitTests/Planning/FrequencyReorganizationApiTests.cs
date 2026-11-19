using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The frequency reorganisation through the real API and database (SQLite here, the same flow on PostgreSQL in <c>PostgresPlanningTests</c>):
/// a proposal is created once for a persistent change, can be declined, accepted and reverted, never touches the sessions that exist, and
/// the next block is generated with the new days and leaves its learning decisions stored.
/// </summary>
public abstract class FrequencyReorganizationApiScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private const string Url = "/api/v1/plans/current/frequency";

    /// <summary>A four-day plan whose user has trained Monday, Wednesday and Friday for five complete weeks.</summary>
    private async Task<(TrazoUser User, HttpClient Client)> ThreeDaysForFiveWeeksAsync()
    {
        var (user, client, _) = await StartPlanAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == user.Id);
        plan.StartDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-35); // Monday, five complete weeks ago
        var sessions = await db.PlannedSessions.Where(s => s.PlanId == plan.Id).OrderBy(s => s.SequenceIndex).Take(15).ToListAsync();
        for (var i = 0; i < sessions.Count; i++)
        {
            var on = plan.StartDate.AddDays(7 * (i / 3) + (i % 3) * 2);
            var at = new DateTimeOffset(on.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            db.WorkoutLogs.Add(new WorkoutLog
            {
                Id = Guid.NewGuid(), UserId = user.Id, PlanId = plan.Id, PlannedSessionId = sessions[i].Id, LoggedAt = at.AddHours(1),
                PerformedAt = at, PerformedOn = on, TimeZoneId = "UTC", Outcome = WorkoutOutcome.Completed, Recovery = "Good"
            });
            sessions[i].Status = PlannedSessionStatus.Completed;
        }
        await db.SaveChangesAsync();
        return (user, client);
    }

    private static string[] Days(JsonElement element) => element.EnumerateArray().Select(d => d.GetString()!).ToArray();

    private async Task<int> CountAsync<T>(Func<TrazoDbContext, IQueryable<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TrazoDbContext>()).CountAsync();
    }

    [Fact]
    public async Task A_regular_user_gets_no_proposal()
    {
        var (_, client, _) = await StartPlanAsync();
        var overview = await JsonAsync(await client.GetAsync(Url));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("pending").ValueKind);
        Assert.NotEqual("PersistentReduction", overview.GetProperty("pattern").GetString());
        Assert.Equal(0, await CountAsync(db => db.FrequencyProposals));
    }

    [Fact]
    public async Task A_persistent_change_creates_one_proposal_and_asking_again_keeps_it()
    {
        var (_, client) = await ThreeDaysForFiveWeeksAsync();
        var first = await JsonAsync(await client.GetAsync(Url));
        Assert.Equal("PersistentReduction", first.GetProperty("pattern").GetString());
        Assert.True(first.GetProperty("pending").ValueKind != JsonValueKind.Null, first.GetProperty("message").ToString());
        var pending = first.GetProperty("pending");
        Assert.Equal(["Monday", "Wednesday", "Friday"], Days(pending.GetProperty("toDays")));
        Assert.Equal(["Monday", "Tuesday", "Thursday", "Friday"], Days(pending.GetProperty("fromDays")));
        Assert.Equal(3, pending.GetProperty("preview").GetProperty("proposedSessionsPerWeek").GetInt32());
        Assert.Contains("no es algo puntual", pending.GetProperty("reason").GetString());
        Assert.Equal("frequency-1.0", pending.GetProperty("policyVersion").GetString());

        var second = await JsonAsync(await client.GetAsync(Url));
        Assert.Equal(pending.GetProperty("id").GetGuid(), second.GetProperty("pending").GetProperty("id").GetGuid());
        Assert.Equal(1, await CountAsync(db => db.FrequencyProposals));

        // The same proposal is announced where the user already looks at the pace.
        var next = await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));
        Assert.Equal(pending.GetProperty("id").GetGuid(), next.GetProperty("frequency").GetProperty("proposalId").GetGuid());
        Assert.Equal(1, await CountAsync(db => db.FrequencyProposals));
    }

    [Fact]
    public async Task Declining_a_proposal_does_not_offer_the_same_days_again_for_a_while()
    {
        var (_, client) = await ThreeDaysForFiveWeeksAsync();
        var id = (await JsonAsync(await client.GetAsync(Url))).GetProperty("pending").GetProperty("id").GetGuid();
        var declined = await client.PostAsync($"{Url}/proposals/{id}/dismiss", null);
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        var again = await JsonAsync(await client.GetAsync(Url));
        Assert.Equal(JsonValueKind.Null, again.GetProperty("pending").ValueKind);
        Assert.Equal("dismissed", again.GetProperty("history")[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/proposals/{id}/accept", null)).StatusCode);
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal(4, plan.GetProperty("trainingDays").GetArrayLength());
    }

    [Fact]
    public async Task Accepting_changes_the_next_block_only_and_reverting_goes_back_keeping_the_history()
    {
        var (_, client) = await ThreeDaysForFiveWeeksAsync();
        var id = (await JsonAsync(await client.GetAsync(Url))).GetProperty("pending").GetProperty("id").GetGuid();
        var sessionsBefore = await CountAsync(db => db.PlannedSessions);
        var logsBefore = await CountAsync(db => db.WorkoutLogs);

        var accepted = await client.PostAsync($"{Url}/proposals/{id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var overview = await JsonAsync(accepted);
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("pending").ValueKind);
        Assert.True(overview.GetProperty("canRevert").GetBoolean());
        Assert.Equal("accepted", overview.GetProperty("history")[0].GetProperty("status").GetString());
        // Nothing that exists was rewritten.
        Assert.Equal(sessionsBefore, await CountAsync(db => db.PlannedSessions));
        Assert.Equal(logsBefore, await CountAsync(db => db.WorkoutLogs));
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal(["Monday", "Wednesday", "Friday"], Days(plan.GetProperty("trainingDays")));
        Assert.True(plan.GetProperty("maxSessionMinutes").GetInt32() <= 75);
        // Accepting twice does nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/proposals/{id}/accept", null)).StatusCode);

        // The block generated from now on has three sessions a week and leaves its learning decisions, once.
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(3, week.GetProperty("sessions").GetArrayLength());
        var decisions = await CountAsync(db => db.LongitudinalDecisions);
        Assert.True(decisions > 0);
        await client.GetAsync("/api/v1/plans/current/week");
        Assert.Equal(decisions, await CountAsync(db => db.LongitudinalDecisions));
        var volume = await JsonAsync(await client.GetAsync("/api/v1/plans/current/volume-decisions"));
        // Week 5 of a 12-week plan is a deload; the block the user is in (week 6) is the first one generated with the new days.
        Assert.All(volume.GetProperty("decisions").EnumerateArray(), d => Assert.True(d.GetProperty("blockSequence").GetInt32() > 1));
        Assert.All(volume.GetProperty("decisions").EnumerateArray(), d => Assert.False(string.IsNullOrWhiteSpace(d.GetProperty("reason").GetString())));

        var reverted = await JsonAsync(await client.PostAsync($"{Url}/revert", null));
        Assert.False(reverted.GetProperty("canRevert").GetBoolean());
        Assert.Equal(2, reverted.GetProperty("history").GetArrayLength());
        var after = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal(["Monday", "Tuesday", "Thursday", "Friday"], Days(after.GetProperty("trainingDays")));
        Assert.Equal(75, after.GetProperty("maxSessionMinutes").GetInt32());
        // The generated block and the logs are still there; reverting again has nothing to revert.
        Assert.Equal(logsBefore, await CountAsync(db => db.WorkoutLogs));
        Assert.True(await CountAsync(db => db.PlannedSessions) >= sessionsBefore);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"{Url}/revert", null)).StatusCode);
    }

    [Fact]
    public async Task The_export_and_the_deletion_cover_the_new_tables()
    {
        var (user, client) = await ThreeDaysForFiveWeeksAsync();
        var id = (await JsonAsync(await client.GetAsync(Url))).GetProperty("pending").GetProperty("id").GetGuid();
        await client.PostAsync($"{Url}/proposals/{id}/accept", null);
        await client.GetAsync("/api/v1/plans/current/week");

        var export = await JsonAsync(await client.GetAsync("/api/v1/account/export"));
        Assert.Equal(1, export.GetProperty("frequencyProposals").GetArrayLength());
        Assert.True(export.GetProperty("longitudinalDecisions").GetArrayLength() > 0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Contains(typeof(FrequencyProposal), Trazo.Infrastructure.Accounts.AccountData.UserOwned);
        Assert.True(await db.FrequencyProposals.AnyAsync(p => p.UserId == user.Id));
    }

    [Fact]
    public async Task Nobody_sees_or_decides_another_users_proposal()
    {
        var (_, owner) = await ThreeDaysForFiveWeeksAsync();
        var id = (await JsonAsync(await owner.GetAsync(Url))).GetProperty("pending").GetProperty("id").GetGuid();
        var stranger = ClientFor(await AddUserAsync("other@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Url)).StatusCode); // no plan of their own
        var other = await StartOtherPlanAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsync($"{Url}/proposals/{id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsync($"{Url}/proposals/{id}/dismiss", null)).StatusCode);
        Assert.True(await CountAsync(db => db.FrequencyProposals.Where(p => p.Id == id && p.Status == FrequencyProposal.Pending)) == 1);
    }

    private async Task<HttpClient> StartOtherPlanAsync()
    {
        var (_, client, _) = await StartPlanAsync("third@example.com");
        return client;
    }
}

public sealed class FrequencyReorganizationApiTests() : FrequencyReorganizationApiScenarios(null);

/// <summary>The same scenarios on a real PostgreSQL with the real migrations (CI job "postgres"; needs TRAZO_TEST_POSTGRES).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresFrequencyReorganizationApiTests() : FrequencyReorganizationApiScenarios(
    Environment.GetEnvironmentVariable("TRAZO_TEST_POSTGRES") ?? throw new InvalidOperationException("TRAZO_TEST_POSTGRES is not set."));
