using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Domain.Identity;
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
        Assert.Equal(2, snapshot.GetProperty("schemaVersion").GetInt32());
        // The first block has nothing to carry from.
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("shadowCarry").ValueKind);
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

    private static readonly string[] AllEquipment = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    /// <summary>A completed workout of the first exercise of the first session: three sets at the given loads (set 1, 2, 3).</summary>
    private async Task<(Guid LogId, string ExerciseId)> LogWorkoutAsync(
        Guid userId, int dayOffset, decimal[] weights, bool generalPain = false, bool setPain = false)
    {
        return await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            var session = (await db.PlannedSessions.Where(s => s.PlanId == plan.Id).OrderBy(s => s.SequenceIndex).ToListAsync())[0];
            var exercise = PrescriptionJson.Read(session.PrescriptionJson).First();
            var at = Now.AddDays(dayOffset);
            var log = new WorkoutLog
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, PlannedSessionId = session.Id, LoggedAt = at.AddHours(1),
                PerformedAt = at, PerformedOn = DateOnly.FromDateTime(at.UtcDateTime), TimeZoneId = "UTC", Outcome = WorkoutOutcome.Completed,
                Recovery = "Good", PainReported = generalPain, Notes = "private note"
            };
            for (var set = 1; set <= weights.Length; set++)
                log.Sets.Add(new WorkoutSetLog
                {
                    Id = Guid.NewGuid(), WorkoutLogId = log.Id, ExerciseId = exercise.ExerciseId, SetNumber = set, WeightKg = weights[set - 1],
                    Repetitions = 8, Rir = 2m, Completed = true, PainReported = setPain && set == 1
                });
            db.WorkoutLogs.Add(log);
            await db.SaveChangesAsync();
            return (log.Id, exercise.ExerciseId);
        });
    }

    /// <summary>Archives the user's plan and creates a new one, whose first block reads the load references from the history.</summary>
    private async Task StartNewPlanAsync(TrazoUser user)
    {
        await ClientFor(user).PostAsync("/api/v1/plans/current/archive", null);
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Planning.ITrainingPlanService>()
            .CreateAsync(user.Id, PlanTestCatalog.Profile(days: 4, equipment: AllEquipment), 42, CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
    }

    private async Task<JsonElement> ActivePlanFirstBlockLoadReferencesAsync(Guid userId)
    {
        var block = await InDbAsync(async db =>
        {
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active);
            return await db.TrainingMesocycles.AsNoTracking().SingleAsync(m => m.PlanId == plan.Id && m.Sequence == 1);
        });
        return Snapshot(block).GetProperty("inputs").GetProperty("loadReferences");
    }

    [ScenarioFact]
    public async Task The_load_references_the_generator_used_are_kept_with_their_source_log_and_values()
    {
        var (user, _, _) = await StartPlanAsync("trace10@example.com");
        var (logId, exercise) = await LogWorkoutAsync(user.Id, -10, [50m, 55m, 52m]);
        await StartNewPlanAsync(user);

        var references = await ActivePlanFirstBlockLoadReferencesAsync(user.Id);
        var item = references.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("exerciseId").GetString() == exercise);
        Assert.Equal(logId, item.GetProperty("workoutLogId").GetGuid());       // relates to the original record
        Assert.Equal(2, item.GetProperty("setNumber").GetInt32());              // the heaviest completed set
        Assert.Equal(55m, item.GetProperty("weightKg").GetDecimal());           // the effective value the generator received
        Assert.Equal(8, item.GetProperty("repetitions").GetInt32());
        Assert.Equal(2m, item.GetProperty("rir").GetDecimal());
        Assert.Equal(3, item.GetProperty("completedSets").GetInt32());
        Assert.Equal(DateOnly.FromDateTime(Now.AddDays(-10).UtcDateTime).ToString("yyyy-MM-dd"), item.GetProperty("performedOn").GetString());
        Assert.False(references.GetProperty("suppressedByPain").GetBoolean());
        var criteria = references.GetProperty("criteria");
        Assert.Equal(40, criteria.GetProperty("logsLimit").GetInt32());
        Assert.Equal(5, criteria.GetProperty("painSessionWindow").GetInt32());
        Assert.Equal(3, criteria.GetProperty("painExposureWindow").GetInt32());
        // Only what the generator consumed: no notes, no other sets.
        Assert.DoesNotContain("private note", (await BlocksAsync()).Last().GenerationSnapshotJson);
        Assert.Equal(["exerciseId", "workoutLogId", "setNumber", "performedOn", "weightKg", "repetitions", "rir", "completedSets"],
            item.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [ScenarioFact]
    public async Task The_references_stay_interpretable_when_the_original_log_is_edited_or_deleted_later()
    {
        var (user, _, _) = await StartPlanAsync("trace11@example.com");
        var (logId, exercise) = await LogWorkoutAsync(user.Id, -10, [50m, 55m, 52m]);
        await StartNewPlanAsync(user);
        var block = await InDbAsync(async db => await db.TrainingMesocycles.AsNoTracking()
            .SingleAsync(m => m.Sequence == 1 && m.GenerationSnapshotJson!.Contains(logId.ToString())));
        var before = block.GenerationSnapshotJson;

        await InDbAsync(async db =>
        {
            foreach (var set in await db.WorkoutSetLogs.Where(s => s.WorkoutLogId == logId).ToListAsync()) set.WeightKg = 99m;
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(before, (await InDbAsync(db => db.TrainingMesocycles.AsNoTracking().SingleAsync(m => m.Id == block.Id))).GenerationSnapshotJson);

        await InDbAsync(async db =>
        {
            db.WorkoutLogs.Remove(await db.WorkoutLogs.SingleAsync(l => l.Id == logId));
            await db.SaveChangesAsync();
            return 0;
        });
        var after = await InDbAsync(db => db.TrainingMesocycles.AsNoTracking().SingleAsync(m => m.Id == block.Id));
        Assert.Equal(before, after.GenerationSnapshotJson);
        var item = Snapshot(after).GetProperty("inputs").GetProperty("loadReferences").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("exerciseId").GetString() == exercise);
        Assert.Equal(55m, item.GetProperty("weightKg").GetDecimal()); // still readable with the log gone
    }

    [ScenarioFact]
    public async Task Pain_that_stops_the_references_is_recorded_as_the_reason_there_are_none()
    {
        var (general, _, _) = await StartPlanAsync("trace12@example.com");
        await LogWorkoutAsync(general.Id, -3, [50m, 50m, 50m], generalPain: true);
        await StartNewPlanAsync(general);
        var suppressed = await ActivePlanFirstBlockLoadReferencesAsync(general.Id);
        Assert.True(suppressed.GetProperty("suppressedByPain").GetBoolean());
        Assert.Equal(0, suppressed.GetProperty("items").GetArrayLength());

        var (local, _, _) = await StartPlanAsync("trace13@example.com");
        var (_, exercise) = await LogWorkoutAsync(local.Id, -3, [50m, 50m, 50m], setPain: true);
        await StartNewPlanAsync(local);
        var withheld = await ActivePlanFirstBlockLoadReferencesAsync(local.Id);
        Assert.Contains(exercise, withheld.GetProperty("withheldForPain").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(withheld.GetProperty("items").EnumerateArray(), i => i.GetProperty("exerciseId").GetString() == exercise);
    }

    [ScenarioFact]
    public async Task A_block_generated_later_records_the_references_that_existed_then_not_the_ones_of_the_first_block()
    {
        var (user, client, _) = await StartPlanAsync("trace14@example.com");
        Assert.Equal(0, Snapshot((await BlocksAsync()).Single()).GetProperty("inputs").GetProperty("loadReferences").GetProperty("items").GetArrayLength());

        var (logId, exercise) = await LogWorkoutAsync(user.Id, -5, [60m, 60m, 60m]);
        await AdvanceToWeekSixAsync(client);

        var blocks = await BlocksAsync();
        var later = blocks.Last();
        var items = Snapshot(later).GetProperty("inputs").GetProperty("loadReferences").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("workoutLogId").GetGuid() == logId && i.GetProperty("exerciseId").GetString() == exercise);
        Assert.Equal(0, Snapshot(blocks[0]).GetProperty("inputs").GetProperty("loadReferences").GetProperty("items").GetArrayLength());
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

    private static TrainingExerciseCandidate Candidate(
        string id = "squat", string version = "1-202601010000", string[]? equipment = null, TrainingGoal[]? goals = null,
        TrainingMuscleGroup[]? primary = null, TrainingMuscleGroup[]? secondary = null) =>
        new(id, version, "Squat", TrainingMovementPattern.Squat, equipment ?? ["barbell"], goals ?? [TrainingGoal.Hypertrophy],
            TrainingExperience.Beginner, 50, true, primary ?? [TrainingMuscleGroup.Quadriceps], secondary ?? []);

    [Fact]
    public void The_canonical_form_is_unambiguous_whatever_characters_the_values_contain()
    {
        // With separators, these pairs of different catalogs would produce the same text; with typed JSON they cannot.
        var pairs = new (TrainingExerciseCandidate[] A, TrainingExerciseCandidate[] B)[]
        {
            ([Candidate(equipment: ["a,b"])], [Candidate(equipment: ["a", "b"])]),
            ([Candidate(equipment: ["a|b"])], [Candidate(equipment: ["a"], version: "b")]),
            ([Candidate(id: "a|b")], [Candidate(id: "a", version: "b|" + "1-202601010000")]),
            ([Candidate(id: "a\n")], [Candidate(id: "a")]),
            ([Candidate(id: "x\"y")], [Candidate(id: "x", version: "y\"")]),
            ([Candidate(equipment: ["a\\", "b"])], [Candidate(equipment: ["a", "\\b"])]),
            ([Candidate(id: "a"), Candidate(id: "b")], [Candidate(id: "a\",\"b")]),
        };
        foreach (var (a, b) in pairs)
        {
            Assert.NotEqual(CatalogFingerprint.Canonicalize(a), CatalogFingerprint.Canonicalize(b));
            Assert.NotEqual(CatalogFingerprint.Compute(a), CatalogFingerprint.Compute(b));
        }
    }

    [Fact]
    public void The_canonical_form_is_a_typed_json_document_sorted_by_id_with_sorted_collections()
    {
        var text = CatalogFingerprint.Canonicalize([Candidate(id: "b", equipment: ["z", "a"]), Candidate(id: "a")]);
        using var json = JsonDocument.Parse(text);
        var exercises = json.RootElement.GetProperty("exercises").EnumerateArray().ToList();
        Assert.Equal(["a", "b"], exercises.Select(e => e.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(["a", "z"], exercises[1].GetProperty("requiredEquipment").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(JsonValueKind.Number, exercises[0].GetProperty("selectionPriority").ValueKind);
        Assert.Equal(JsonValueKind.True, exercises[0].GetProperty("isReviewed").ValueKind);
        Assert.Equal(3, json.RootElement.GetProperty("fingerprintVersion").GetInt32());
    }

    [Fact]
    public void Every_field_the_generator_selects_on_changes_the_fingerprint()
    {
        var baseline = Candidate();
        var variants = new Dictionary<string, TrainingExerciseCandidate>
        {
            ["id"] = baseline with { ExerciseId = "squat2" },
            ["catalogVersion"] = baseline with { CatalogVersion = "2-202701010000" },
            ["movementPattern"] = baseline with { MovementPattern = TrainingMovementPattern.Hinge },
            ["minimumExperience"] = baseline with { MinimumExperience = TrainingExperience.Advanced },
            ["selectionPriority"] = baseline with { SelectionPriority = 51 },
            ["isReviewed"] = baseline with { IsReviewed = false },
            ["requiredEquipment"] = baseline with { RequiredEquipment = ["barbell", "bench"] },
            ["suitableGoals"] = baseline with { SuitableGoals = [TrainingGoal.Hypertrophy, TrainingGoal.Strength] },
            ["primaryMuscles"] = baseline with { PrimaryMuscleGroups = [TrainingMuscleGroup.Glutes] },
            ["secondaryMuscles"] = baseline with { SecondaryMuscleGroups = [TrainingMuscleGroup.Glutes] },
            ["allowedTechniques"] = baseline with { AllowedTechniques = [TrainingTechniqueType.DropSet] }
        };
        var reference = CatalogFingerprint.Compute([baseline]);
        foreach (var (field, changed) in variants)
            Assert.NotEqual(reference, CatalogFingerprint.Compute([changed]));
        Assert.Equal(variants.Count, variants.Select(v => CatalogFingerprint.Compute([v.Value])).Distinct().Count());
    }

    [Fact]
    public void The_trace_records_the_version_of_the_generation_it_describes_and_refuses_one_without_a_version()
    {
        var profile = PlanTestCatalog.Profile(days: 3);
        var catalog = PlanTestCatalog.Candidates();
        var slot = new MesocycleSlot(1, 1, "accumulation", 1, 4);
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        MesocycleGeneration By(string version) => new([new GeneratedSession(1, null, null, "A", 60, "{}")], [], null, version);

        var older = MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By("0.10.0"), 0, false, null, LoadReferenceSelection.None, null, now);
        var newer = MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By("0.11.1"), 0, false, null, LoadReferenceSelection.None, null, now);

        Assert.Equal("0.10.0", older.EngineVersion);
        Assert.Equal("0.11.1", newer.EngineVersion);
        Assert.Contains("\"engineVersion\":\"0.10.0\"", older.SnapshotJson);
        Assert.Equal(older.CatalogFingerprint, newer.CatalogFingerprint);
        Assert.Throws<InvalidOperationException>(() =>
            MesocycleGenerationTrace.Capture(profile, 7, slot, catalog, By(""), 0, false, null, LoadReferenceSelection.None, null, now));
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
            MesocycleGenerationTrace.Capture(one, 7, slot, catalog, generation, 0, false, null, LoadReferenceSelection.None, null, now).SnapshotJson,
            MesocycleGenerationTrace.Capture(other, 7, slot, catalog, generation, 0, false, null, LoadReferenceSelection.None, null, now).SnapshotJson);
    }
}
