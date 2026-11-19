using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Traceability of the prescription, through the real service and database (SQLite, and the same on real PostgreSQL in CI): the original
/// prescription of a generated session survives every later change, and every change (the engine's or the user's) leaves a record with the
/// exact prescription before and after, saved in the same transaction as the change itself.
/// </summary>
public abstract class PrescriptionHistoryScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private async Task<T> InDbAsync<T>(Func<TrazoDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    private Task<List<PlannedSession>> SessionsAsync() =>
        InDbAsync(db => db.PlannedSessions.AsNoTracking().OrderBy(s => s.SequenceIndex).ToListAsync());

    private Task<List<PlanAdaptation>> RowsAsync(string? kind = null) =>
        InDbAsync(db => db.PlanAdaptations.AsNoTracking().Where(a => kind == null || a.Kind == kind).OrderBy(a => a.Id).ToListAsync());

    private static string Url(Guid session, string tail) => $"/api/v1/plans/current/sessions/{session}/{tail}";

    /// <summary>A session of the plan that contains the exercise, plus a valid replacement for it.</summary>
    private async Task<(PlannedSession Session, string Exercise, string Replacement)> SwapTargetAsync(HttpClient client, int skip = 0)
    {
        var session = (await SessionsAsync()).Where(s => s.Status == PlannedSessionStatus.Planned).Skip(skip).First();
        var exercise = PrescriptionJson.Read(session.PrescriptionJson)[0].ExerciseId;
        var listing = await client.GetAsync(Url(session.Id, $"exercises/{exercise}/alternatives"));
        var replacement = (await JsonAsync(listing)).GetProperty("alternatives")[0].GetProperty("exerciseId").GetString()!;
        return (session, exercise, replacement);
    }

    private static string[] ExerciseIds(string json) => PrescriptionJson.Read(json).Select(e => e.ExerciseId).ToArray();

    /// <summary>
    /// Replays the records of one session on top of its original prescription: every record must start where the previous one ended, so the
    /// whole history is a chain from what the engine generated to what the session holds now, with no gap and no step missing.
    /// </summary>
    private static void AssertChain(PlannedSession session, IEnumerable<PlanAdaptation> rows)
    {
        var steps = rows.Where(r => r.ChangesJson is not null)
            .SelectMany(r => PrescriptionChangeTrace.Read(r.ChangesJson!).Where(c => c.SessionId == session.Id)).ToList();
        var state = JsonNode.Parse(session.OriginalPrescriptionJson!)!;
        while (steps.Count > 0)
        {
            var next = steps.FirstOrDefault(c => JsonNode.DeepEquals(JsonNode.Parse(c.Before!), state));
            Assert.NotNull(next);
            state = JsonNode.Parse(next!.After!)!;
            steps.Remove(next);
        }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(session.PrescriptionJson), state), "the history ends where the session is now");
    }

    /// <summary>Three stressed sessions in a row make the engine turn the next one into a deload.</summary>
    private async Task StressThreeSessionsAsync(HttpClient client)
    {
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        foreach (var session in week.GetProperty("sessions").EnumerateArray().Take(3))
        {
            var sets = session.GetProperty("exercises").EnumerateArray().Select(e => new
            {
                exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = 1, weightKg = 50m, repetitions = 6, rir = 0, completed = true
            }).ToArray();
            var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
                new { outcome = "Partial", nonCompletionReason = "Fatigue", recovery = "Insufficient", sessionRpe = 10, sets });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------ the original prescription

    [ScenarioFact]
    public async Task A_generated_session_stores_the_prescription_the_engine_produced_and_it_is_exactly_the_one_it_starts_with()
    {
        await StartPlanAsync("hist1@example.com");
        var sessions = await SessionsAsync();

        Assert.NotEmpty(sessions);
        Assert.All(sessions, s =>
        {
            Assert.Equal(s.PrescriptionJson, s.OriginalPrescriptionJson);
            Assert.Equal(PrescriptionChangeTrace.SnapshotSchemaVersion, s.OriginalPrescriptionSchemaVersion);
            Assert.Equal(PrescriptionSources.Engine, s.PrescriptionSource);
        });
        Assert.Empty(await RowsAsync()); // creating a plan changes nothing yet: no history row
    }

    [ScenarioFact]
    public async Task An_automatic_adaptation_changes_the_prescription_but_the_original_stays_and_the_record_says_the_engine_did_it()
    {
        var (_, client, _) = await StartPlanAsync("hist2@example.com");
        var before = await SessionsAsync();
        await StressThreeSessionsAsync(client);

        var after = await SessionsAsync();
        var changed = after.Where(s => s.PrescriptionJson != before.Single(b => b.Id == s.Id).PrescriptionJson).ToList();
        Assert.NotEmpty(changed);
        Assert.All(after, s => Assert.Equal(before.Single(b => b.Id == s.Id).OriginalPrescriptionJson, s.OriginalPrescriptionJson));
        Assert.All(changed, s => Assert.NotEqual(s.OriginalPrescriptionJson, s.PrescriptionJson));

        var deload = Assert.Single(await RowsAsync(), r => r.Kind == "deload");
        Assert.Equal(PrescriptionSources.Engine, deload.Source);
        Assert.Equal(TrainingProgramGenerator.EngineVersion, deload.EngineVersion);
        Assert.Equal(PrescriptionChangeTrace.SchemaVersion, deload.SchemaVersion);
        Assert.NotNull(deload.WorkoutLogId);   // the workout that triggered it
        Assert.NotNull(deload.EvidenceAt);     // and when that workout happened
        Assert.False(string.IsNullOrWhiteSpace(deload.Explanation));
        var recorded = PrescriptionChangeTrace.Read(deload.ChangesJson!);
        Assert.Equal(deload.SessionsChanged, recorded.Count);
        foreach (var change in recorded)
        {
            Assert.Equal(PrescriptionChangeTrace.Modified, change.Operation);
            var session = after.Single(s => s.Id == change.SessionId);
            Assert.Equal(session.MesocycleId, change.MesocycleId);
            Assert.NotEqual(change.Before, change.After); // a recorded change really changed something
        }
        foreach (var session in changed) AssertChain(session, await RowsAsync());
    }

    [ScenarioFact]
    public async Task The_original_is_immutable_even_if_code_tries_to_overwrite_it()
    {
        await StartPlanAsync("hist3@example.com");
        await Assert.ThrowsAnyAsync<Exception>(() => InDbAsync(async db =>
        {
            var session = await db.PlannedSessions.FirstAsync();
            session.OriginalPrescriptionJson = "{}";
            await db.SaveChangesAsync();
            return 0;
        }));
    }

    // ------------------------------------------------------------------ user changes

    [ScenarioFact]
    public async Task A_swap_records_the_exercise_that_left_the_one_that_came_and_the_prescription_before_and_after()
    {
        var (_, client, _) = await StartPlanAsync("hist4@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);

        var swapped = await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement });
        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);

        var row = Assert.Single(await RowsAsync(TrainingPlanService.SwapKind));
        Assert.Equal(PrescriptionSources.User, row.Source);
        Assert.Null(row.EngineVersion);          // a user decision names no engine
        Assert.Null(row.WorkoutLogId);
        Assert.Equal([exercise, replacement], row.ExerciseIds);
        Assert.Equal(1, row.SessionsChanged);
        var change = Assert.Single(PrescriptionChangeTrace.Read(row.ChangesJson!));
        Assert.Equal(session.Id, change.SessionId);
        Assert.Equal(session.MesocycleId, change.MesocycleId);
        Assert.Contains(exercise, ExerciseIds(change.Before!));
        Assert.DoesNotContain(replacement, ExerciseIds(change.Before!));
        Assert.Contains(replacement, ExerciseIds(change.After!));
        Assert.DoesNotContain(exercise, ExerciseIds(change.After!));

        var now = (await SessionsAsync()).Single(s => s.Id == session.Id);
        Assert.Equal(session.OriginalPrescriptionJson, now.OriginalPrescriptionJson);
        Assert.Contains(exercise, ExerciseIds(now.OriginalPrescriptionJson!));
        AssertChain(now, await RowsAsync());
    }

    [ScenarioFact]
    public async Task A_swap_applied_to_the_rest_of_the_block_records_each_session_it_reached_and_only_those()
    {
        var (_, client, _) = await StartPlanAsync("hist5@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        var before = await SessionsAsync();

        var swapped = await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"),
            new { replacementExerciseId = replacement, applyToRemainingBlock = true });
        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);

        var after = await SessionsAsync();
        var reached = after.Where(s => s.PrescriptionJson != before.Single(b => b.Id == s.Id).PrescriptionJson).Select(s => s.Id).ToHashSet();
        Assert.True(reached.Count > 1, "the exercise repeats later in the block");
        var row = Assert.Single(await RowsAsync(TrainingPlanService.SwapKind)); // one operation, one record
        Assert.Equal(reached.Count, row.SessionsChanged);
        var changes = PrescriptionChangeTrace.Read(row.ChangesJson!);
        Assert.Equal(reached.OrderBy(i => i), changes.Select(c => c.SessionId).OrderBy(i => i));
        Assert.All(changes, c => Assert.Equal(PrescriptionChangeTrace.Modified, c.Operation));
        Assert.All(changes, c => Assert.Equal(before.Single(b => b.Id == c.SessionId).PrescriptionJson, c.Before!, JsonComparer));
        foreach (var id in reached) AssertChain(after.Single(s => s.Id == id), [row]);
    }

    [ScenarioFact]
    public async Task An_added_exercise_is_recorded_as_a_user_change_with_the_prescription_before_and_after()
    {
        var (_, client, _) = await StartPlanAsync("hist6@example.com");
        var session = (await SessionsAsync()).First();
        var extra = new[] { "push-up", "squat", "ohp", "plank", "lunge" }.First(id => !ExerciseIds(session.PrescriptionJson).Contains(id));

        var added = await client.PostAsJsonAsync(Url(session.Id, "exercises"), new { exerciseId = extra, sets = 2 });
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());

        var row = Assert.Single(await RowsAsync(TrainingPlanService.AddKind));
        Assert.Equal(PrescriptionSources.User, row.Source);
        Assert.Equal([extra], row.ExerciseIds);
        var change = Assert.Single(PrescriptionChangeTrace.Read(row.ChangesJson!));
        Assert.DoesNotContain(extra, ExerciseIds(change.Before!));
        Assert.Contains(extra, ExerciseIds(change.After!));
        Assert.Equal(session.PrescriptionJson, change.Before!, JsonComparer);
    }

    [ScenarioFact]
    public async Task Several_changes_in_a_row_keep_the_whole_history_from_the_original_to_what_the_session_holds_now()
    {
        var (_, client, _) = await StartPlanAsync("hist7@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        var extra = new[] { "push-up", "squat", "ohp", "plank", "lunge", "dip" }.First(id =>
            !ExerciseIds(session.PrescriptionJson).Contains(id) && id != replacement);

        Assert.True((await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement })).IsSuccessStatusCode);
        Assert.True((await client.PostAsJsonAsync(Url(session.Id, "exercises"), new { exerciseId = extra, sets = 2 })).IsSuccessStatusCode);
        var writtenSwap = (await RowsAsync(TrainingPlanService.SwapKind)).Single().ChangesJson;
        await StressThreeSessionsAsync(client); // the engine acts on top of the user's edits

        var rows = await RowsAsync();
        Assert.Equal(PrescriptionSources.User, rows.Single(r => r.Kind == TrainingPlanService.SwapKind).Source);
        Assert.Contains(rows, r => r.Source == PrescriptionSources.Engine && r.ChangesJson is not null);
        // Nothing earlier was rewritten by what came later: the first records are exactly as they were written.
        Assert.Equal(writtenSwap, rows.Single(r => r.Kind == TrainingPlanService.SwapKind).ChangesJson);
        var now = (await SessionsAsync()).Single(s => s.Id == session.Id);
        AssertChain(now, rows);
        Assert.Contains(exercise, ExerciseIds(now.OriginalPrescriptionJson!));
    }

    // ------------------------------------------------------------------ concurrent edits

    [ScenarioFact]
    public async Task Concurrent_edits_of_the_same_session_must_not_silently_overwrite_each_other()
    {
        await StartPlanAsync("hist-concurrency@example.com");
        var sessionId = (await SessionsAsync()).First().Id;

        // Two independent request scopes load the same row before either one saves. This is a deterministic
        // interleaving of the read/modify/write sequence used by SwapExerciseAsync and AddExerciseAsync.
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var first = await firstDb.PlannedSessions.SingleAsync(s => s.Id == sessionId);
        var second = await secondDb.PlannedSessions.SingleAsync(s => s.Id == sessionId);
        var original = first.PrescriptionJson;
        Assert.Equal(original, second.PrescriptionJson);

        static string WithDistinctSets(string json, int sets)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            root["exercises"]!.AsArray()[0]!.AsObject()["sets"] = sets;
            return root.ToJsonString();
        }

        var firstAfter = WithDistinctSets(original, 17);
        var secondAfter = WithDistinctSets(original, 19);
        var now = DateTimeOffset.UtcNow;

        first.PrescriptionJson = firstAfter;
        firstDb.PlanAdaptations.Add(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = first.PlanId, Kind = "user_manual_plan_edit", ExerciseIds = [],
            SessionsChanged = 1, Explanation = "Concurrent edit A", AppliedAt = now, Source = PrescriptionSources.User,
            SchemaVersion = PrescriptionChangeTrace.SchemaVersion,
            ChangesJson = PrescriptionChangeTrace.Serialize([
                new SessionChange(PrescriptionChangeTrace.Modified, first.Id, first.MesocycleId, first.WeekNumber,
                    first.SequenceIndex, original, firstAfter)
            ])
        });
        await firstDb.SaveChangesAsync();

        // The second context still holds the original value. A correct concurrency guard must reject this stale write,
        // rather than accepting it and leaving two history entries that branch from the same before-state.
        second.PrescriptionJson = secondAfter;
        secondDb.PlanAdaptations.Add(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = second.PlanId, Kind = "user_manual_plan_edit", ExerciseIds = [],
            SessionsChanged = 1, Explanation = "Concurrent edit B", AppliedAt = now.AddTicks(1), Source = PrescriptionSources.User,
            SchemaVersion = PrescriptionChangeTrace.SchemaVersion,
            ChangesJson = PrescriptionChangeTrace.Serialize([
                new SessionChange(PrescriptionChangeTrace.Modified, second.Id, second.MesocycleId, second.WeekNumber,
                    second.SequenceIndex, original, secondAfter)
            ])
        });

        var conflict = await Record.ExceptionAsync(() => secondDb.SaveChangesAsync());
        Assert.IsType<DbUpdateConcurrencyException>(conflict);

        var persisted = (await SessionsAsync()).Single(s => s.Id == sessionId);
        Assert.Equal(firstAfter, persisted.PrescriptionJson);
        Assert.Single(await RowsAsync("user_manual_plan_edit"));
        AssertChain(persisted, await RowsAsync());
    }

    // ------------------------------------------------------------------ manual plans

    private static object Manual(string name, params (string Title, string Exercise, int Sets)[] days) => new
    {
        name, totalWeeks = 4, schedulingMode = "Sequence",
        days = days.Select(d => new { title = d.Title, exercises = new object[] { new { exerciseId = d.Exercise, sets = d.Sets, targetRir = 2 } } }).ToArray()
    };

    [ScenarioFact]
    public async Task A_manual_plan_is_marked_as_the_users_and_never_gets_an_engine_snapshot_even_after_it_is_edited()
    {
        var user = await AddUserAsync("hist8@example.com");
        var client = ClientFor(user);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", Manual("Rutina", ("A", "bench-press", 4), ("B", "squat", 4)))).StatusCode);

        var created = await SessionsAsync();
        Assert.All(created, s =>
        {
            Assert.Null(s.OriginalPrescriptionJson);
            Assert.Null(s.OriginalPrescriptionSchemaVersion);
            Assert.Equal(PrescriptionSources.User, s.PrescriptionSource);
        });
        Assert.Empty(await RowsAsync()); // nothing was changed yet

        var edited = await client.PutAsJsonAsync("/api/v1/plans/current/manual", Manual("Rutina 2", ("A", "bench-press", 3), ("B", "squat", 3)));
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());

        var after = await SessionsAsync();
        Assert.All(after, s =>
        {
            Assert.Null(s.OriginalPrescriptionJson);
            Assert.Equal(PrescriptionSources.User, s.PrescriptionSource);
        });
        var row = Assert.Single(await RowsAsync(TrainingPlanService.ManualEditKind));
        Assert.Equal(PrescriptionSources.User, row.Source);
        Assert.Null(row.EngineVersion);
        var changes = PrescriptionChangeTrace.Read(row.ChangesJson!);
        // Every session that was replaced is recorded with what it prescribed, and every new one with what it prescribes.
        Assert.Equal(created.Select(s => s.Id).OrderBy(i => i), changes.Where(c => c.Operation == PrescriptionChangeTrace.Removed).Select(c => c.SessionId).OrderBy(i => i));
        Assert.Equal(after.Select(s => s.Id).OrderBy(i => i), changes.Where(c => c.Operation == PrescriptionChangeTrace.Added).Select(c => c.SessionId).OrderBy(i => i));
        Assert.All(changes.Where(c => c.Operation == PrescriptionChangeTrace.Removed), c =>
            Assert.Equal(created.Single(s => s.Id == c.SessionId).PrescriptionJson, c.Before!, JsonComparer));
        Assert.All(changes.Where(c => c.Operation == PrescriptionChangeTrace.Added), c =>
            Assert.Equal(after.Single(s => s.Id == c.SessionId).PrescriptionJson, c.After!, JsonComparer));
    }

    // ------------------------------------------------------------------ atomicity and retries

    [ScenarioFact]
    public async Task If_the_history_cannot_be_written_the_prescription_change_is_not_committed_either()
    {
        var (_, client, _) = await StartPlanAsync("hist9@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        var before = (await SessionsAsync()).Single(s => s.Id == session.Id);
        await InDbAsync(db => db.Database.ExecuteSqlRawAsync(postgres is null
            ? "CREATE TRIGGER fail_history BEFORE INSERT ON plan_adaptations WHEN NEW.\"Kind\" = 'user_swap_exercise' BEGIN SELECT RAISE(ABORT, 'history write failed'); END;"
            : "CREATE FUNCTION fail_history() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'history write failed'; END $$ LANGUAGE plpgsql;"
              + " CREATE TRIGGER fail_history BEFORE INSERT ON plan_adaptations FOR EACH ROW WHEN (NEW.\"Kind\" = 'user_swap_exercise') EXECUTE FUNCTION fail_history();"));

        HttpResponseMessage? response = null;
        try { response = await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement }); }
        catch (Exception) { /* the host surfaces the failure as an exception or as a 500: either way nothing may be committed */ }
        if (response is not null) Assert.False(response.IsSuccessStatusCode);

        var after = (await SessionsAsync()).Single(s => s.Id == session.Id);
        Assert.Equal(before.PrescriptionJson, after.PrescriptionJson);
        Assert.Empty(await RowsAsync());
    }

    [ScenarioFact]
    public async Task Repeating_a_request_never_writes_a_second_record()
    {
        var (_, client, _) = await StartPlanAsync("hist10@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        var swap = () => client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement });
        Assert.Equal(HttpStatusCode.OK, (await swap()).StatusCode);
        Assert.False((await swap()).IsSuccessStatusCode); // it is already done
        Assert.Single(await RowsAsync(TrainingPlanService.SwapKind));

        var other = (await SessionsAsync()).First(s => s.Id != session.Id);
        var extra = new[] { "push-up", "squat", "ohp", "plank", "lunge", "dip" }.First(id => !ExerciseIds(other.PrescriptionJson).Contains(id));
        var add = () => client.PostAsJsonAsync(Url(other.Id, "exercises"), new { exerciseId = extra, sets = 2 });
        Assert.True((await add()).IsSuccessStatusCode);
        Assert.False((await add()).IsSuccessStatusCode);
        Assert.Single(await RowsAsync(TrainingPlanService.AddKind));
    }

    [ScenarioFact]
    public async Task Saving_the_same_manual_routines_again_changes_no_prescription_so_it_leaves_no_record()
    {
        var client = ClientFor(await AddUserAsync("hist11@example.com"));
        var plan = Manual("Rutina", ("A", "bench-press", 4), ("B", "squat", 4));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", plan)).StatusCode);

        Assert.True((await client.PutAsJsonAsync("/api/v1/plans/current/manual", plan)).IsSuccessStatusCode);
        Assert.True((await client.PutAsJsonAsync("/api/v1/plans/current/manual", plan)).IsSuccessStatusCode);

        Assert.Empty(await RowsAsync(TrainingPlanService.ManualEditKind));
    }

    // ------------------------------------------------------------------ history that predates the trace

    [ScenarioFact]
    public async Task Sessions_and_adaptations_without_a_trace_stay_valid_and_nothing_is_invented_for_them()
    {
        var (_, client, _) = await StartPlanAsync("hist12@example.com");
        // Make the data look like it did before the trace existed.
        await InDbAsync(async db =>
        {
            // Raw SQL: an older database simply never had the values (the immutability guard only stops code from rewriting them).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE planned_sessions SET \"OriginalPrescriptionJson\" = NULL, \"OriginalPrescriptionSchemaVersion\" = NULL, \"PrescriptionSource\" = NULL");
            return 0;
        });
        Assert.All(await SessionsAsync(), s => { Assert.Null(s.OriginalPrescriptionJson); Assert.Null(s.PrescriptionSource); });

        // Everything still works for them, and the change that follows is traced from now on without pretending an original exists.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/plans/current/week")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/plans/current/adaptations")).StatusCode);
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement })).StatusCode);
        var stored = (await SessionsAsync()).Single(s => s.Id == session.Id);
        Assert.Null(stored.OriginalPrescriptionJson);
        Assert.Single(await RowsAsync(TrainingPlanService.SwapKind));
    }

    [ScenarioFact]
    public async Task Adaptation_rows_written_before_the_trace_read_as_without_source_version_or_changes()
    {
        var (user, client, _) = await StartPlanAsync("hist13@example.com");
        var planId = await InDbAsync(db => db.TrainingPlans.Select(p => p.Id).SingleAsync());
        await InDbAsync(async db =>
        {
            db.PlanAdaptations.Add(new PlanAdaptation
            {
                Id = Guid.NewGuid(), PlanId = planId, Kind = "reduction_sets", ExerciseIds = ["squat"], SessionsChanged = 2,
                Explanation = "Antiguo", AppliedAt = Now.AddDays(-3), EvidenceAt = Now.AddDays(-3)
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var old = Assert.Single(await RowsAsync("reduction_sets"));
        Assert.Null(old.Source);
        Assert.Null(old.EngineVersion);
        Assert.Null(old.SchemaVersion);
        Assert.Null(old.ChangesJson);
        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations?whole=true"));
        Assert.Contains(history.GetProperty("entries").EnumerateArray(), e => e.GetProperty("kind").GetString() == "reduction_sets");
        Assert.NotNull(user);
    }

    // ------------------------------------------------------------------ contracts

    [ScenarioFact]
    public async Task The_user_facing_api_does_not_show_the_new_history_and_keeps_its_shape()
    {
        var (_, client, _) = await StartPlanAsync("hist14@example.com");
        var weekBefore = await (await client.GetAsync("/api/v1/plans/current/week")).Content.ReadAsStringAsync();
        var historyBefore = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations"));

        var (session, exercise, replacement) = await SwapTargetAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement })).StatusCode);

        var week = await (await client.GetAsync("/api/v1/plans/current/week")).Content.ReadAsStringAsync();
        foreach (var internalName in new[] { "originalPrescription", "prescriptionSource", "changesJson", "schemaVersion", "engineVersion", "source" })
            Assert.DoesNotContain($"\"{internalName}\"", week, StringComparison.OrdinalIgnoreCase);
        // The user's own edit is not one of the engine's decisions: the history the user reads is exactly what it was.
        var historyAfter = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations"));
        Assert.Equal(historyBefore.GetProperty("entries").GetArrayLength(), historyAfter.GetProperty("entries").GetArrayLength());
        Assert.Equal(historyBefore.GetProperty("changes").GetInt32(), historyAfter.GetProperty("changes").GetInt32());
        Assert.Equal(historyBefore.GetProperty("sessionsAdjusted").GetInt32(), historyAfter.GetProperty("sessionsAdjusted").GetInt32());
        Assert.Equal(historyBefore.EnumerateObject().Select(x => x.Name).Order(), historyAfter.EnumerateObject().Select(x => x.Name).Order());
        Assert.NotEmpty(weekBefore);
    }

    [ScenarioFact]
    public async Task A_user_edit_does_not_start_an_engine_cooldown_or_count_as_an_engine_adaptation()
    {
        var (_, client, _) = await StartPlanAsync("hist15@example.com");
        var (session, exercise, replacement) = await SwapTargetAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Url(session.Id, $"exercises/{exercise}/swap"), new { replacementExerciseId = replacement })).StatusCode);

        // The three stressed sessions still trigger the deload: the swap record is not read as a recent engine adaptation.
        await StressThreeSessionsAsync(client);
        Assert.Single(await RowsAsync("deload"));
    }

    private static readonly IEqualityComparer<string> JsonComparerImpl = new JsonEquality();
    private static IEqualityComparer<string> JsonComparer => JsonComparerImpl;

    private sealed class JsonEquality : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => x is not null && y is not null && JsonNode.DeepEquals(JsonNode.Parse(x), JsonNode.Parse(y));
        public int GetHashCode(string value) => 0;
    }
}

public sealed class PrescriptionHistoryTests() : PrescriptionHistoryScenarios(null);

/// <summary>The same scenarios on real PostgreSQL with the real migrations (skipped when TRAZO_TEST_POSTGRES is not set).</summary>
[Trait("Category", "Postgres")]
public sealed class PostgresPrescriptionHistoryTests() : PrescriptionHistoryScenarios(PostgresSettings.ConnectionString);
