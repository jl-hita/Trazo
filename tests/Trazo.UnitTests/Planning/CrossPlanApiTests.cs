using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Learning across plans through the real API and database (SQLite here, the same scenarios on PostgreSQL below): a new plan starts
/// from what the user's earlier plans showed, never from another user's, never twice, and it all goes away with the account.
/// The earlier plan's observations are seeded as the observer would have stored them, so the difference measured is what creating
/// the new plan does with them.
/// </summary>
public abstract class CrossPlanApiScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private const string Password = "CorrectHorse1Battery";

    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    /// <summary>Makes the user's only plan an old one and stores <paramref name="blocks"/> blocks of observations for it.</summary>
    private async Task AgeAndObserveAsync(Guid userId, int blocks, bool favorable, int daysAgo)
    {
        var baselines = MesocycleGenerator.Baselines(PlanTestCatalog.Profile(days: 4, minutes: 75));
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            plan.CreatedAt = Now.AddDays(-daysAgo - 7 * 4 * blocks);
            plan.UpdatedAt = plan.CreatedAt;
            for (var block = 1; block <= blocks; block++)
                for (var week = 1; week <= 4; week++)
                    foreach (var b in baselines)
                        db.MuscleWeekObservations.Add(new MuscleWeekObservation
                        {
                            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = (block - 1) * 4 + week, MesocycleSequence = block,
                            IsDeload = false, Muscle = b.Muscle.ToString(), Quality = "valid", PrescribedSets = b.Target, PerformedSets = b.Target,
                            SessionsPlanned = 4, SessionsDone = 4, Exposures = 4, CompletedSets = (int)b.Target, BelowRangeSets = favorable ? 0 : 3,
                            PerformanceChangePercent = favorable ? 2.5m : -3m, PerformanceSamples = 4, FatigueSignals = favorable ? 0 : 2,
                            ProcessedThrough = Now.AddDays(-daysAgo - 7 * 4 * (blocks - block) - 4 + week), UpdatedAt = Now
                        });
            await db.SaveChangesAsync();
            return 0;
        });
    }

    private static readonly string[] Equipment = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    private static PlanProfile ProfileOf(int days) => PlanTestCatalog.Profile(days: days, equipment: Equipment);

    /// <summary>The user archives the finished plan and creates another through the real service with a FIXED seed, so two users who
    /// differ only in their history get blocks that differ only because of it.</summary>
    private async Task<HttpClient> NewPlanAsync(TrazoUser user, PlanProfile? profile = null)
    {
        var client = ClientFor(user);
        await client.PostAsync("/api/v1/plans/current/archive", null);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Planning.ITrainingPlanService>()
            .CreateAsync(user.Id, profile ?? ProfileOf(4), 42, CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
        return client;
    }

    private static List<JsonElement> Decisions(JsonElement view) => view.GetProperty("decisions").EnumerateArray().ToList();

    private static async Task<JsonElement> VolumeAsync(HttpClient client) => await JsonAsync(await client.GetAsync("/api/v1/plans/current/volume-decisions"));

    /// <summary>Effective weekly sets of every muscle in week 2 of the user's active plan, read from the stored prescriptions (what the user will really train).</summary>
    private async Task<SortedDictionary<string, decimal>> WeeklyVolumeAsync(Guid userId) => await InDbAsync(async db =>
    {
        var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active);
        var sessions = (await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.WeekNumber == 2).ToListAsync())
            .Select(s => PrescriptionJson.Read(s.PrescriptionJson));
        return new SortedDictionary<string, decimal>(Longitudinal.PlanSimulator.WeeklyEffective(sessions).ToDictionary(p => p.Key.ToString(), p => p.Value));
    });

    private static IEnumerable<string> Muscles(IEnumerable<JsonElement> decisions, Func<decimal, decimal, bool> change) =>
        decisions.Where(d => change(d.GetProperty("proposedSets").GetDecimal(), d.GetProperty("baselineSets").GetDecimal())).Select(d => d.GetProperty("muscle").GetString()!);

    [Fact]
    public async Task A_favorable_history_gives_the_new_plan_a_higher_starting_reference_than_a_user_without_history()
    {
        var (veteran, _, _) = await StartPlanAsync("veteran@example.com");
        await AgeAndObserveAsync(veteran.Id, blocks: 4, favorable: true, daysAgo: 10);
        var rookie = await AddUserAsync("rookie@example.com");

        var veteranClient = await NewPlanAsync(veteran);
        var rookieClient = await NewPlanAsync(rookie);

        var inherited = Decisions(await VolumeAsync(veteranClient));
        Assert.NotEmpty(inherited);
        Assert.Contains(inherited, d => d.GetProperty("action").GetString() == "carry" && d.GetProperty("proposedSets").GetDecimal() > d.GetProperty("baselineSets").GetDecimal());
        Assert.All(inherited, d =>
        {
            Assert.Equal("longitudinal-1.2", d.GetProperty("policyVersion").GetString());
            Assert.InRange(d.GetProperty("proposedSets").GetDecimal() - d.GetProperty("baselineSets").GetDecimal(), -3m, 1m);
            Assert.False(string.IsNullOrWhiteSpace(d.GetProperty("reason").GetString()));
        });
        // Nothing of the veteran leaks to the user without history: no decision, and the plain generator's volume.
        Assert.Empty(Decisions(await VolumeAsync(rookieClient)));
        var withHistory = await WeeklyVolumeAsync(veteran.Id);
        var without = await WeeklyVolumeAsync(rookie.Id);
        var raised = Muscles(inherited, (proposed, baseline) => proposed > baseline).ToList();
        Assert.NotEmpty(raised);
        // Same seed, same profile: the only difference is the history, and it reaches the sets the user will train.
        Assert.Contains(raised, m => withHistory.GetValueOrDefault(m) > without.GetValueOrDefault(m));
        Assert.NotEqual(without, withHistory);
    }

    [Fact]
    public async Task A_poor_history_does_not_start_the_new_plan_higher_and_the_block_has_fewer_sets()
    {
        var (user, _, _) = await StartPlanAsync("struggled@example.com");
        await AgeAndObserveAsync(user.Id, blocks: 3, favorable: false, daysAgo: 10);
        var other = await AddUserAsync("plain@example.com");
        var client = await NewPlanAsync(user);
        var plain = await NewPlanAsync(other);

        var decisions = Decisions(await VolumeAsync(client));
        Assert.DoesNotContain(decisions, d => d.GetProperty("proposedSets").GetDecimal() > d.GetProperty("baselineSets").GetDecimal());
        Assert.Contains(decisions, d => d.GetProperty("proposedSets").GetDecimal() < d.GetProperty("baselineSets").GetDecimal());
        var lowered = Muscles(decisions, (proposed, baseline) => proposed < baseline).ToList();
        var mine = await WeeklyVolumeAsync(user.Id);
        var plainVolume = await WeeklyVolumeAsync(other.Id);
        Assert.Contains(lowered, m => mine.GetValueOrDefault(m) < plainVolume.GetValueOrDefault(m));
        Assert.DoesNotContain(mine, p => p.Value > plainVolume.GetValueOrDefault(p.Key) + 1m);
    }

    [Fact]
    public async Task A_history_older_than_the_expiry_changes_nothing()
    {
        var (user, _, _) = await StartPlanAsync("old@example.com");
        await AgeAndObserveAsync(user.Id, blocks: 4, favorable: true, daysAgo: LongitudinalPolicyDays + 30);
        var other = await AddUserAsync("fresh@example.com");
        var client = await NewPlanAsync(user);
        var plain = await NewPlanAsync(other);
        Assert.All(Decisions(await VolumeAsync(client)), d => Assert.Equal(d.GetProperty("baselineSets").GetDecimal(), d.GetProperty("proposedSets").GetDecimal()));
        Assert.Equal(await WeeklyVolumeAsync(other.Id), await WeeklyVolumeAsync(user.Id));
    }

    private const int LongitudinalPolicyDays = Trazo.Application.Planning.Longitudinal.LongitudinalPolicy.CarryOverExpiryDays;

    [Fact]
    public async Task A_plan_with_another_goal_is_not_learned_from()
    {
        var (user, _, _) = await StartPlanAsync("strong@example.com", Profile(goal: "Strength"));
        await AgeAndObserveAsync(user.Id, blocks: 4, favorable: true, daysAgo: 10);
        var client = await NewPlanAsync(user);
        Assert.Empty(Decisions(await VolumeAsync(client)));
    }

    [Fact]
    public async Task A_new_plan_with_fewer_days_does_not_inherit_the_gain_of_the_four_day_plan()
    {
        var (user, _, _) = await StartPlanAsync("fewer@example.com");
        await AgeAndObserveAsync(user.Id, blocks: 4, favorable: true, daysAgo: 10);
        var client = await NewPlanAsync(user, ProfileOf(3));
        var decisions = Decisions(await VolumeAsync(client));
        Assert.DoesNotContain(decisions, d => d.GetProperty("proposedSets").GetDecimal() > d.GetProperty("baselineSets").GetDecimal());
    }

    [Fact]
    public async Task Reading_and_regenerating_never_duplicates_observations_or_decisions()
    {
        var (user, _, _) = await StartPlanAsync("retry@example.com");
        await AgeAndObserveAsync(user.Id, blocks: 4, favorable: true, daysAgo: 10);
        var client = await NewPlanAsync(user);
        var observations = await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == user.Id));
        var decisions = await InDbAsync(db => db.LongitudinalDecisions.CountAsync(d => d.UserId == user.Id));
        Assert.True(decisions > 0);
        for (var i = 0; i < 3; i++)
        {
            await client.GetAsync("/api/v1/plans/current/week");
            await client.GetAsync("/api/v1/plans/current/volume-decisions");
        }
        Assert.Equal(observations, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == user.Id)));
        Assert.Equal(decisions, await InDbAsync(db => db.LongitudinalDecisions.CountAsync(d => d.UserId == user.Id)));
        // One row per block and muscle.
        var grouped = await InDbAsync(async db => (await db.LongitudinalDecisions.Where(d => d.UserId == user.Id).ToListAsync())
            .GroupBy(d => (d.PlanId, d.MesocycleSequence, d.Muscle)).Max(g => g.Count()));
        Assert.Equal(1, grouped);
    }

    [Fact]
    public async Task Each_users_rows_belong_to_them_and_the_account_deletion_removes_the_reusable_history()
    {
        var (ana, _, _) = await StartPlanAsync("ana2@example.com");
        await AgeAndObserveAsync(ana.Id, blocks: 4, favorable: true, daysAgo: 10);
        var anaClient = await NewPlanAsync(ana);
        var (bob, bobClient, _) = await StartPlanAsync("bob2@example.com");
        await AgeAndObserveAsync(bob.Id, blocks: 3, favorable: false, daysAgo: 10);
        await NewPlanAsync(bob);

        // Their decisions never cite the other's plans, and what each inherited differs because the histories differ.
        await InDbAsync(async db =>
        {
            var anaPlans = await db.TrainingPlans.Where(p => p.UserId == ana.Id).Select(p => p.Id).ToListAsync();
            Assert.All(await db.LongitudinalDecisions.Where(d => d.UserId == ana.Id).ToListAsync(), d => Assert.Contains(d.PlanId, anaPlans));
            Assert.All(await db.MuscleWeekObservations.Where(o => o.UserId == ana.Id).ToListAsync(), o => Assert.Contains(o.PlanId, anaPlans));
            return 0;
        });
        Assert.Contains(Decisions(await VolumeAsync(anaClient)), d => d.GetProperty("proposedSets").GetDecimal() > d.GetProperty("baselineSets").GetDecimal());
        Assert.DoesNotContain(Decisions(await VolumeAsync(bobClient)), d => d.GetProperty("proposedSets").GetDecimal() > d.GetProperty("baselineSets").GetDecimal());

        var export = await JsonAsync(await anaClient.GetAsync("/api/v1/account/export"));
        Assert.True(export.GetProperty("longitudinalDecisions").GetArrayLength() > 0);
        Assert.True(export.GetProperty("muscleObservations").GetArrayLength() > 0);
        var bobObservations = await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == bob.Id));

        // The SQLite schema of this fixture only has the planning tables, which the full account deletion needs beyond; there the same
        // deletion is checked with the complete schema in AccountApiTests. Here it runs against the real PostgreSQL migrations.
        if (postgres is null) return;
        var deleted = await anaClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account") { Content = JsonContent.Create(new { password = Password }) });
        Assert.True(deleted.StatusCode == HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        Assert.Equal(0, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == ana.Id)));
        Assert.Equal(0, await InDbAsync(db => db.LongitudinalDecisions.CountAsync(d => d.UserId == ana.Id)));
        Assert.Equal(0, await InDbAsync(db => db.TrainingPlans.CountAsync(p => p.UserId == ana.Id)));
        // Nobody else lost anything.
        Assert.Equal(bobObservations, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == bob.Id)));
        Assert.NotEmpty(Decisions(await VolumeAsync(bobClient)));
    }
}

public sealed class CrossPlanApiTests() : CrossPlanApiScenarios(null);

/// <summary>The same scenarios on a real PostgreSQL with the real migrations (CI job "postgres"; needs TRAZO_TEST_POSTGRES).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresCrossPlanApiTests() : CrossPlanApiScenarios(
    Environment.GetEnvironmentVariable("TRAZO_TEST_POSTGRES") ?? throw new InvalidOperationException("TRAZO_TEST_POSTGRES is not set."));
