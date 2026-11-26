using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// What a new plan does with the history of the user's earlier plans: nothing. It starts from its own baseline, so creating a plan stays
/// simple and predictable; the observations of the earlier plans are kept (and exported, and deleted with the account) but no decision
/// taken in another plan reaches the first block of this one. Through the real API and database, on SQLite and on real PostgreSQL.
/// </summary>
public abstract class NewPlanApiScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private const string Password = "CorrectHorse1Battery";
    private static readonly string[] Equipment = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    /// <summary>Stores <paramref name="blocks"/> blocks of observations for the user's only plan, favourable or poor.</summary>
    private async Task ObserveAsync(Guid userId, int blocks, bool favorable)
    {
        var baselines = MesocycleGenerator.Baselines(PlanTestCatalog.Profile(days: 4, minutes: 75));
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            for (var block = 1; block <= blocks; block++)
                for (var week = 1; week <= 4; week++)
                    foreach (var b in baselines)
                        db.MuscleWeekObservations.Add(new MuscleWeekObservation
                        {
                            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = (block - 1) * 4 + week, MesocycleSequence = block,
                            IsDeload = false, Muscle = b.Muscle.ToString(), Quality = "valid", PrescribedSets = b.Target, PerformedSets = b.Target,
                            SessionsPlanned = 4, SessionsDone = 4, Exposures = 4, CompletedSets = (int)b.Target, BelowRangeSets = favorable ? 0 : 3,
                            PerformanceChangePercent = favorable ? 2.5m : -3m, PerformanceSamples = 4, FatigueSignals = favorable ? 0 : 2,
                            ProcessedThrough = Now.AddDays(-30 + (block - 1) * 7 + week), UpdatedAt = Now
                        });
            await db.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>The user archives the finished plan and creates another through the real service with a FIXED seed, so two users who
    /// differ only in their history get blocks that differ only because of it.</summary>
    private async Task<HttpClient> NewPlanAsync(TrazoUser user)
    {
        var client = ClientFor(user);
        await client.PostAsync("/api/v1/plans/current/archive", null);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Planning.ITrainingPlanService>()
            .CreateAsync(user.Id, PlanTestCatalog.Profile(days: 4, equipment: Equipment), 42, CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
        return client;
    }

    private async Task<SortedDictionary<string, decimal>> WeeklyVolumeAsync(Guid userId) => await InDbAsync(async db =>
    {
        var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active);
        var sessions = (await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.WeekNumber == 2).ToListAsync())
            .Select(s => PrescriptionJson.Read(s.PrescriptionJson));
        return new SortedDictionary<string, decimal>(Longitudinal.PlanSimulator.WeeklyEffective(sessions).ToDictionary(p => p.Key.ToString(), p => p.Value));
    });

    [ScenarioFact]
    public async Task A_new_plan_is_the_same_whatever_the_earlier_plans_showed()
    {
        var (favorable, _, _) = await StartPlanAsync("favorable@example.com");
        await ObserveAsync(favorable.Id, blocks: 4, favorable: true);
        var (poor, _, _) = await StartPlanAsync("poor@example.com");
        await ObserveAsync(poor.Id, blocks: 3, favorable: false);
        var rookie = await AddUserAsync("rookie@example.com");

        var favorableClient = await NewPlanAsync(favorable);
        var poorClient = await NewPlanAsync(poor);
        var rookieClient = await NewPlanAsync(rookie);

        foreach (var client in new[] { favorableClient, poorClient, rookieClient })
            Assert.Empty((await JsonAsync(await client.GetAsync("/api/v1/plans/current/volume-decisions"))).GetProperty("decisions").EnumerateArray());
        var plain = await WeeklyVolumeAsync(rookie.Id);
        Assert.Equal(plain, await WeeklyVolumeAsync(favorable.Id));
        Assert.Equal(plain, await WeeklyVolumeAsync(poor.Id));
    }

    [ScenarioFact]
    public async Task The_observations_of_earlier_plans_are_kept_exported_and_deleted_with_the_account()
    {
        var (ana, _, _) = await StartPlanAsync("ana2@example.com");
        await ObserveAsync(ana.Id, blocks: 2, favorable: true);
        var anaClient = await NewPlanAsync(ana);
        var (bob, bobClient, _) = await StartPlanAsync("bob2@example.com");
        await ObserveAsync(bob.Id, blocks: 2, favorable: false);

        var kept = await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == ana.Id));
        Assert.True(kept > 0, "archiving a plan and creating another must not delete what was observed");
        Assert.All(await InDbAsync(db => db.MuscleWeekObservations.Where(o => o.UserId == ana.Id).ToListAsync()),
            o => Assert.NotEqual(bob.Id, o.UserId));

        var export = await JsonAsync(await anaClient.GetAsync("/api/v1/account/export"));
        Assert.True(export.GetProperty("muscleObservations").GetArrayLength() > 0);
        var bobObservations = await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == bob.Id));

        // The SQLite schema of this fixture only has the planning tables, which the full account deletion needs beyond; there the same
        // deletion is checked with the complete schema in AccountApiTests. Here it runs against the real PostgreSQL migrations.
        if (!OnPostgres) return;
        var deleted = await anaClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account") { Content = JsonContent.Create(new { password = Password }) });
        Assert.True(deleted.StatusCode == HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        Assert.Equal(0, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == ana.Id)));
        Assert.Equal(bobObservations, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == bob.Id)));
    }
}

public sealed class NewPlanApiTests() : NewPlanApiScenarios(null);

/// <summary>The same scenarios on a real PostgreSQL with the real migrations (CI job "postgres"; skipped when TRAZO_TEST_POSTGRES is not set).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresNewPlanApiTests() : NewPlanApiScenarios(PostgresSettings.ConnectionString);
