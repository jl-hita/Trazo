using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Traceability of each generated block, through the real service and database (SQLite, and the same on real PostgreSQL in CI):
/// what a block stores about how it was generated must stay exactly as it was when the plan, the catalog or the engine change later.
/// </summary>
public abstract class MesocycleTraceabilityScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    private Task<List<TrainingMesocycle>> BlocksAsync() =>
        InDbAsync(db => db.TrainingMesocycles.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync());

    /// <summary>The user reaches week 6, so the next blocks are generated the first time the week is read.</summary>
    private async Task AdvanceToWeekSixAsync(HttpClient client)
    {
        await InDbAsync(async db =>
        {
            (await db.TrainingPlans.SingleAsync()).StartDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-35);
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.True((await client.GetAsync("/api/v1/plans/current/week")).IsSuccessStatusCode);
    }

    private static JsonElement Snapshot(TrainingMesocycle block) => JsonDocument.Parse(block.GenerationSnapshotJson!).RootElement;

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [ScenarioFact]
    public async Task A_new_block_keeps_the_context_engine_catalog_and_time_it_was_generated_with()
    {
        await StartPlanAsync("trace1@example.com");
        var block = (await BlocksAsync()).Single();

        Assert.Equal(MesocycleGenerationTrace.SnapshotSchemaVersion, block.SnapshotSchemaVersion);
        Assert.Equal(TrainingProgramGenerator.EngineVersion, block.EngineVersion);
        Assert.Equal(Now, block.GeneratedAt);
        Assert.Matches("^[0-9a-f]{64}$", block.CatalogFingerprint!);
        Assert.Equal(CatalogFingerprint.Compute(PlanTestCatalog.Candidates()), block.CatalogFingerprint);

        var snapshot = Snapshot(block);
        Assert.Equal(1, snapshot.GetProperty("schemaVersion").GetInt32());
        var profile = snapshot.GetProperty("profile");
        Assert.Equal("Hypertrophy", profile.GetProperty("goal").GetString());
        Assert.Equal("Intermediate", profile.GetProperty("experience").GetString());
        Assert.Equal(4, profile.GetProperty("trainingDays").GetArrayLength());
        Assert.Equal(75, profile.GetProperty("maxSessionMinutes").GetInt32());
        Assert.True(snapshot.GetProperty("seed").ValueKind == JsonValueKind.Number);
        Assert.Equal(1, snapshot.GetProperty("block").GetProperty("sequence").GetInt32());
        Assert.False(snapshot.GetProperty("learning").GetProperty("applied").GetBoolean());
        Assert.Equal(TrainingProgramGenerator.EngineVersion, snapshot.GetProperty("engineVersion").GetString());
    }

    [ScenarioFact]
    public async Task Changing_the_plan_afterwards_never_changes_the_snapshot_of_a_block_already_generated()
    {
        var (_, client, _) = await StartPlanAsync("trace2@example.com");
        var before = (await BlocksAsync()).Single();

        // Everything the generator reads from the plan changes, legitimately, after the block exists.
        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync();
            plan.Goal = "Strength";
            plan.Experience = "Advanced";
            plan.TrainingDays = ["Monday", "Wednesday"];
            plan.MaxSessionMinutes = 45;
            plan.AvailableEquipment = ["dumbbell"];
            plan.Limitations = ["knee"];
            plan.MusclePriorities = ["Chest:Emphasize"];
            plan.IncludeDeloadPhases = false;
            plan.DeloadEveryTrainingWeeks = 6;
            plan.Seed += 1;
            plan.EngineVersion = "someone-edited-this";
            await db.SaveChangesAsync();
            return 0;
        });
        await client.GetAsync("/api/v1/plans/current/week");

        var after = (await BlocksAsync()).First(m => m.Id == before.Id);
        Assert.Equal(before.GenerationSnapshotJson, after.GenerationSnapshotJson);
        Assert.Equal(before.EngineVersion, after.EngineVersion);
        Assert.Equal(before.CatalogFingerprint, after.CatalogFingerprint);
        Assert.Equal(before.GeneratedAt, after.GeneratedAt);
        Assert.Equal("Hypertrophy", Snapshot(after).GetProperty("profile").GetProperty("goal").GetString());
    }

    [ScenarioFact]
    public async Task A_block_generated_later_captures_the_plan_as_it_is_then_and_the_first_block_keeps_the_original()
    {
        var (_, client, _) = await StartPlanAsync("trace3@example.com");
        var first = (await BlocksAsync()).Single();

        await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync();
            plan.TrainingDays = ["Monday", "Thursday"];
            plan.MaxSessionMinutes = 60;
            plan.MusclePriorities = ["Chest:Emphasize"];
            plan.Limitations = ["knee"];
            await db.SaveChangesAsync();
            return 0;
        });
        await AdvanceToWeekSixAsync(client);

        var blocks = await BlocksAsync();
        Assert.True(blocks.Count > 1, "reading week 6 generates the pending blocks");
        var later = blocks.Last();
        var profile = Snapshot(later).GetProperty("profile");
        Assert.Equal(["Monday", "Thursday"], Strings(profile.GetProperty("trainingDays")));
        Assert.Equal(60, profile.GetProperty("maxSessionMinutes").GetInt32());
        Assert.Equal("Emphasize", profile.GetProperty("musclePriorities").GetProperty("Chest").GetString());
        Assert.Contains("knee", Strings(profile.GetProperty("limitations")));
        Assert.Equal(Now, later.GeneratedAt);
        Assert.True(Snapshot(later).GetProperty("block").GetProperty("sequence").GetInt32() > 1);

        // The block generated at creation still tells the original story.
        var original = blocks.First(m => m.Id == first.Id);
        Assert.Equal(first.GenerationSnapshotJson, original.GenerationSnapshotJson);
        Assert.Equal(4, Snapshot(original).GetProperty("profile").GetProperty("trainingDays").GetArrayLength());
    }

    [ScenarioFact]
    public async Task Each_block_keeps_the_engine_version_that_generated_it_not_the_one_of_the_plan_or_of_today()
    {
        var (_, client, _) = await StartPlanAsync("trace4@example.com");
        // Simulates a block generated by an earlier deployment (raw SQL: the application itself can never rewrite a trace).
        await InDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE training_mesocycles SET \"EngineVersion\" = '0.10.0', \"GenerationSnapshotJson\" = REPLACE(\"GenerationSnapshotJson\", '\"engineVersion\":\"" +
            TrainingProgramGenerator.EngineVersion + "\"', '\"engineVersion\":\"0.10.0\"')"));
        await AdvanceToWeekSixAsync(client);

        var blocks = await BlocksAsync();
        Assert.True(blocks.Count > 1);
        Assert.Equal("0.10.0", blocks[0].EngineVersion);
        Assert.Equal("0.10.0", Snapshot(blocks[0]).GetProperty("engineVersion").GetString());
        Assert.All(blocks.Skip(1), b =>
        {
            Assert.Equal(TrainingProgramGenerator.EngineVersion, b.EngineVersion);
            Assert.Equal(TrainingProgramGenerator.EngineVersion, Snapshot(b).GetProperty("engineVersion").GetString());
        });
    }

    [ScenarioFact]
    public async Task A_block_keeps_the_fingerprint_of_the_catalog_it_was_generated_with_even_after_the_catalog_changes()
    {
        var (_, client, _) = await StartPlanAsync("trace5@example.com");
        var first = (await BlocksAsync()).Single();

        await InDbAsync(async db =>
        {
            // An administrator re-curates an exercise the generator can see.
            var item = await db.ExerciseCatalog.FirstAsync(e => e.Id == "bodyweight-squat");
            item.SelectionPriority += 7;
            item.UpdatedAt = Now.AddDays(3);
            await db.SaveChangesAsync();
            return 0;
        });
        await AdvanceToWeekSixAsync(client);

        var blocks = await BlocksAsync();
        Assert.Equal(first.CatalogFingerprint, blocks.First(m => m.Id == first.Id).CatalogFingerprint);
        Assert.All(blocks.Skip(1), b => Assert.NotEqual(first.CatalogFingerprint, b.CatalogFingerprint));
    }

    [ScenarioFact]
    public async Task Blocks_generated_before_the_trace_existed_have_no_trace_and_keep_working()
    {
        var (_, client, _) = await StartPlanAsync("trace6@example.com");
        // The state of a block created by the previous version: no trace at all (the migration adds the columns as NULL).
        await InDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE training_mesocycles SET \"GenerationSnapshotJson\" = NULL, \"SnapshotSchemaVersion\" = NULL, \"EngineVersion\" = NULL, \"CatalogFingerprint\" = NULL, \"GeneratedAt\" = NULL"));

        var week = await client.GetAsync("/api/v1/plans/current/week");
        Assert.True(week.IsSuccessStatusCode);
        Assert.Equal(4, (await JsonAsync(week)).GetProperty("sessions").GetArrayLength());
        var historical = (await BlocksAsync()).Single();
        Assert.Null(historical.GenerationSnapshotJson);
        Assert.Null(historical.EngineVersion);
        Assert.Null(historical.CatalogFingerprint);
        Assert.Null(historical.GeneratedAt);

        // The plan continues: the next block is generated, with a trace, and the historical one is not touched or invented.
        await AdvanceToWeekSixAsync(client);
        var blocks = await BlocksAsync();
        Assert.True(blocks.Count > 1);
        Assert.Null(blocks[0].GenerationSnapshotJson);
        Assert.All(blocks.Skip(1), b => Assert.NotNull(b.GenerationSnapshotJson));
    }

    [ScenarioFact]
    public async Task Reading_again_never_duplicates_a_block_nor_rewrites_its_trace()
    {
        var (_, client, _) = await StartPlanAsync("trace7@example.com");
        await AdvanceToWeekSixAsync(client);
        var first = await BlocksAsync();

        for (var i = 0; i < 3; i++) Assert.True((await client.GetAsync("/api/v1/plans/current/week")).IsSuccessStatusCode);
        var again = await BlocksAsync();

        Assert.Equal(first.Count, again.Count);
        Assert.Equal(first.Select(b => b.Sequence).Distinct().Count(), again.Count);
        Assert.Equal(
            first.Select(b => (b.Id, b.GenerationSnapshotJson, b.EngineVersion, b.CatalogFingerprint, b.GeneratedAt)),
            again.Select(b => (b.Id, b.GenerationSnapshotJson, b.EngineVersion, b.CatalogFingerprint, b.GeneratedAt)));
    }

    [ScenarioFact]
    public async Task The_application_cannot_overwrite_a_trace_and_a_partial_trace_is_rejected_by_the_database()
    {
        await StartPlanAsync("trace8@example.com");
        await Assert.ThrowsAnyAsync<Exception>(() => InDbAsync(async db =>
        {
            (await db.TrainingMesocycles.SingleAsync()).EngineVersion = "rewritten";
            await db.SaveChangesAsync();
            return 0;
        }));
        Assert.Equal(TrainingProgramGenerator.EngineVersion, (await BlocksAsync()).Single().EngineVersion);

        await Assert.ThrowsAnyAsync<Exception>(() => InDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE training_mesocycles SET \"EngineVersion\" = NULL")));
        Assert.Equal(TrainingProgramGenerator.EngineVersion, (await BlocksAsync()).Single().EngineVersion);
    }

    [ScenarioFact]
    public async Task The_trace_is_not_exposed_to_the_client()
    {
        var (_, client, week) = await StartPlanAsync("trace9@example.com");
        var text = week.GetRawText() + await (await client.GetAsync("/api/v1/plans/current")).Content.ReadAsStringAsync();
        foreach (var internalName in new[] { "snapshot", "fingerprint", "engineVersion", "generatedAt" })
            Assert.DoesNotContain(internalName, text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MesocycleTraceabilityTests() : MesocycleTraceabilityScenarios(null);

/// <summary>The same scenarios on a real PostgreSQL with the real migrations (CI job "postgres"; skipped when TRAZO_TEST_POSTGRES is not set).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresMesocycleTraceabilityTests() : MesocycleTraceabilityScenarios(PostgresSettings.ConnectionString);

/// <summary>The pure parts: the fingerprint and the recorded version, without a database.</summary>
public sealed class MesocycleTraceUnitTests
{
    [Fact]
    public void The_catalog_fingerprint_does_not_depend_on_the_order_the_rows_are_read_in()
    {
        var catalog = PlanTestCatalog.Candidates().ToList();
        var expected = CatalogFingerprint.Compute(catalog);

        Assert.Equal(expected, CatalogFingerprint.Compute(catalog.AsEnumerable().Reverse()));
        Assert.Equal(expected, CatalogFingerprint.Compute(catalog.OrderBy(c => c.Name)));
        Assert.Equal(expected, CatalogFingerprint.Compute(catalog.OrderBy(c => Guid.NewGuid())));
        // Lists inside an exercise are a set, not a sequence.
        var shuffled = catalog.Select(c => c with { RequiredEquipment = c.RequiredEquipment.Reverse().ToList(), PrimaryMuscleGroups = c.PrimaryMuscleGroups.Reverse().ToList() });
        Assert.Equal(expected, CatalogFingerprint.Compute(shuffled));
    }

    [Fact]
    public void The_catalog_fingerprint_changes_when_the_catalog_the_generator_sees_changes()
    {
        var catalog = PlanTestCatalog.Candidates().ToList();
        var baseline = CatalogFingerprint.Compute(catalog);

        Assert.NotEqual(baseline, CatalogFingerprint.Compute(catalog.Skip(1)));
        Assert.NotEqual(baseline, CatalogFingerprint.Compute(catalog.Select((c, i) => i == 0 ? c with { SelectionPriority = c.SelectionPriority + 1 } : c)));
        Assert.NotEqual(baseline, CatalogFingerprint.Compute(catalog.Select((c, i) => i == 0 ? c with { RequiredEquipment = [.. c.RequiredEquipment, "cable"] } : c)));
        Assert.NotEqual(baseline, CatalogFingerprint.Compute(catalog.Select((c, i) => i == 0 ? c with { CatalogVersion = "2-202701010000" } : c)));
    }

    [Fact]
    public void The_trace_records_the_version_of_the_generation_it_describes_and_refuses_one_without_a_version()
    {
        var profile = PlanTestCatalog.Profile(days: 3);
        var catalog = PlanTestCatalog.Candidates();
        var slot = new MesocycleSlot(1, 1, "accumulation", 1, 4);
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        MesocycleGeneration By(string version) => new([new GeneratedSession(1, null, null, "A", 60, "{}")], [], null, version);

        var older = MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By("0.10.0"), 0, false, null, 0, null, now);
        var newer = MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By("0.11.1"), 0, false, null, 0, null, now);

        Assert.Equal("0.10.0", older.EngineVersion);
        Assert.Equal("0.11.1", newer.EngineVersion);
        Assert.Contains("\"engineVersion\":\"0.10.0\"", older.SnapshotJson);
        Assert.Equal(older.CatalogFingerprint, newer.CatalogFingerprint);
        Assert.Throws<InvalidOperationException>(() =>
            MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By(""), 0, false, null, 0, null, now));
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_snapshot_whatever_the_order_of_the_lists()
    {
        var slot = new MesocycleSlot(1, 1, "accumulation", 1, 4);
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        var catalog = PlanTestCatalog.Candidates();
        var generation = new MesocycleGeneration([new GeneratedSession(1, null, null, "A", 60, "{}")], [], null, "0.11.1");
        var one = PlanTestCatalog.Profile(days: 3, equipment: ["dumbbell", "bench"], limitations: ["knee", "shoulder"]);
        var other = one with { AvailableEquipment = ["bench", "dumbbell"], Limitations = ["shoulder", "knee"], TrainingDays = one.TrainingDays.Reverse().ToList() };

        Assert.Equal(
            MesocycleGenerationTrace.Capture(one, 7, slot, catalog, generation, 0, false, null, 0, null, now).SnapshotJson,
            MesocycleGenerationTrace.Capture(other, 7, slot, catalog, generation, 0, false, null, 0, null, now).SnapshotJson);
    }
}
