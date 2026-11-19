using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The longitudinal layer through the real service and database (SQLite, and the same on real PostgreSQL in CI): the block in progress is
/// never rewritten, a plan that must not learn does not, and one user's history never reaches another user's plan.
/// </summary>
public abstract class LongitudinalApiScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    /// <summary>Four weeks of poor observations (falling performance with fatigue) for every muscle of the user's only plan, block 1.</summary>
    private async Task ObservePoorBlockAsync(Guid userId)
    {
        var baselines = MesocycleGenerator.Baselines(PlanTestCatalog.Profile(days: 4, minutes: 75));
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            for (var week = 1; week <= 4; week++)
                foreach (var b in baselines)
                    db.MuscleWeekObservations.Add(new MuscleWeekObservation
                    {
                        Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = week, MesocycleSequence = 1, IsDeload = false,
                        Muscle = b.Muscle.ToString(), Quality = "valid", PrescribedSets = b.Target, PerformedSets = b.Target, SessionsPlanned = 4,
                        SessionsDone = 4, Exposures = 4, CompletedSets = (int)b.Target, BelowRangeSets = 3, PerformanceChangePercent = -3m,
                        PerformanceSamples = 4, FatigueSignals = 2, ProcessedThrough = Now.AddDays(-30 + week), UpdatedAt = Now
                    });
            await db.SaveChangesAsync();
            return 0;
        });
    }

    [ScenarioFact]
    public async Task The_responses_an_existing_client_reads_keep_every_field_they_had()
    {
        var (_, client, _) = await StartPlanAsync("contract@example.com");
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        // The fields the Android app's PlanView reads (Models.kt), plus the ones the engine returned before this change.
        foreach (var field in new[] { "id", "status", "timeline", "currentWeek", "totalWeeks", "startDate", "endDate", "goal", "experience", "split", "trainingDays",
                     "maxSessionMinutes", "availableEquipment", "excludedExerciseIds", "limitations", "currentPhase", "macrocycle", "disclaimer", "origin",
                     "schedulingMode", "sessionsPerPeriod", "musclePriorities" })
            Assert.True(plan.TryGetProperty(field, out _), $"/plans/current lost '{field}'");
        Assert.True(plan.TryGetProperty("priorityDelivery", out _)); // the only addition: null without priorities

        var next = await JsonAsync(await client.GetAsync("/api/v1/plans/current/next"));
        foreach (var field in new[] { "planId", "schedulingMode", "timeline", "session", "position", "availability", "availabilityLocked", "overdue", "reentry", "reschedule", "adjustment" })
            Assert.True(next.TryGetProperty(field, out _), $"/plans/current/next lost '{field}'");
        var frequency = next.GetProperty("frequency");
        foreach (var field in new[] { "state", "observed", "nominal", "suggestedSessionsPerWeek", "message" })
            Assert.True(frequency.TryGetProperty(field, out _), $"/plans/current/next frequency lost '{field}'");
        Assert.Contains(frequency.GetProperty("state").GetString(), new[] { "Unknown", "OnTrack", "Below", "Above" });
    }

    private static readonly MesocycleSlot ThirdBlock = new(3, 1, "accumulation", 6, 4);

    [ScenarioFact]
    public async Task Generating_the_next_block_never_rewrites_the_block_in_progress()
    {
        var (user, client, _) = await StartPlanAsync("inprogress@example.com");
        await ObservePoorBlockAsync(user.Id);
        await InDbAsync(async db => { (await db.TrainingPlans.SingleAsync(p => p.UserId == user.Id)).StartDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-35); await db.SaveChangesAsync(); return 0; });
        var firstBlock = await InDbAsync(async db => (await db.PlannedSessions.Where(s => s.WeekNumber <= 4).OrderBy(s => s.SequenceIndex).ToListAsync())
            .Select(s => (s.Id, s.PrescriptionJson, s.Status)).ToList());

        // The user is now in week 6: the next blocks get generated, with the learning decisions of the finished one.
        await client.GetAsync("/api/v1/plans/current/week");
        var decisions = await JsonAsync(await client.GetAsync("/api/v1/plans/current/volume-decisions"));
        Assert.Contains(decisions.GetProperty("decisions").EnumerateArray(), d => d.GetProperty("action").GetString() == "decrease");
        Assert.All(decisions.GetProperty("decisions").EnumerateArray(), d =>
        {
            Assert.True(d.GetProperty("blockSequence").GetInt32() > 1);
            Assert.True(d.GetProperty("proposedSets").GetDecimal() <= d.GetProperty("baselineSets").GetDecimal());
        });

        var after = await InDbAsync(async db => (await db.PlannedSessions.Where(s => s.WeekNumber <= 4).OrderBy(s => s.SequenceIndex).ToListAsync())
            .Select(s => (s.Id, s.PrescriptionJson, s.Status)).ToList());
        Assert.Equal(firstBlock, after); // not one set of the finished block changed
    }

    [ScenarioFact]
    public async Task A_plan_that_must_not_learn_does_not_even_read_the_history()
    {
        var (user, _, _) = await StartPlanAsync("nolearn@example.com");
        await ObservePoorBlockAsync(user.Id);
        var profile = PlanTestCatalog.Profile(days: 4, minutes: 75);
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
            var proposal = await LongitudinalLearning.ProposeAsync(db, plan, ThirdBlock, profile, Now, CancellationToken.None);
            Assert.NotEmpty(proposal.LearnedTargets); // positive control: this plan, generated hypertrophy, does learn

            var manual = await db.TrainingPlans.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
            manual.Origin = TrainingPlanOrigin.Manual;
            Assert.Empty((await LongitudinalLearning.ProposeAsync(db, manual, ThirdBlock, profile, Now, CancellationToken.None)).LearnedTargets);

            var strength = await db.TrainingPlans.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
            Assert.Empty((await LongitudinalLearning.ProposeAsync(db, strength, ThirdBlock, profile with { Goal = TrainingGoal.Strength }, Now, CancellationToken.None)).LearnedTargets);

            var deload = ThirdBlock with { PhaseType = "deload", Weeks = 1 };
            Assert.Empty((await LongitudinalLearning.ProposeAsync(db, plan, deload, profile, Now, CancellationToken.None)).LearnedTargets);
            return 0;
        });
    }

    [ScenarioFact]
    public async Task The_history_of_one_user_never_reaches_the_plan_of_another()
    {
        var (poor, _, _) = await StartPlanAsync("poor5@example.com");
        await ObservePoorBlockAsync(poor.Id);
        var (other, _, _) = await StartPlanAsync("other5@example.com");
        var profile = PlanTestCatalog.Profile(days: 4, minutes: 75);
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.AsNoTracking().SingleAsync(p => p.UserId == other.Id);
            var proposal = await LongitudinalLearning.ProposeAsync(db, plan, ThirdBlock, profile, Now, CancellationToken.None);
            Assert.Empty(proposal.LearnedTargets);
            Assert.Empty(proposal.Decisions);
            return 0;
        });
        // reading the other plan neither wrote rows for it nor touched the first user's
        Assert.Equal(16, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == poor.Id && o.Muscle == "Chest")) * 4);
        Assert.Equal(0, await InDbAsync(db => db.MuscleWeekObservations.CountAsync(o => o.UserId == other.Id)));
    }
}

public sealed class LongitudinalApiTests() : LongitudinalApiScenarios(null);

/// <summary>The same scenarios on a real PostgreSQL with the real migrations (CI job "postgres"; skipped when TRAZO_TEST_POSTGRES is not set).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresLongitudinalApiTests() : LongitudinalApiScenarios(PostgresSettings.ConnectionString);
