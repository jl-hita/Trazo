using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The learning migrations on a real PostgreSQL: they apply on an empty database, create the indexes the code relies on (the unique ones
/// that make every update idempotent, the partial one that allows a single pending proposal per plan), cascade with the user and the plan,
/// and go down and up again. Runs only in the CI job "postgres" (TRAZO_TEST_POSTGRES).
/// </summary>
[Trait("Category", "Postgres")]
public sealed class PostgresMigrationTests : IDisposable
{
    private const string BeforeLearning = "20261124100000_PlanMusclePriorities";
    private readonly string _admin = PostgresSettings.ConnectionString;
    private readonly string _name = $"trazo_mig_{Guid.NewGuid():N}";
    private readonly string _connection;

    public PostgresMigrationTests()
    {
        Execute(_admin, $"CREATE DATABASE \"{_name}\"");
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _name }.ConnectionString;
    }

    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        Execute(_admin, $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)");
    }

    private static void Execute(string connection, string sql)
    {
        using var c = new NpgsqlConnection(connection);
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Query(string connection, string sql)
    {
        using var c = new NpgsqlConnection(connection);
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    }

    private TrazoDbContext Context() => new(new DbContextOptionsBuilder<TrazoDbContext>().UseNpgsql(_connection).Options);

    private static readonly string[] LearningTables = ["muscle_week_observations", "longitudinal_decisions", "frequency_proposals"];

    [ScenarioFact]
    public void The_migrations_apply_on_an_empty_database_with_the_expected_indexes_and_cascades()
    {
        using (var db = Context()) db.Database.Migrate();
        using (var db = Context()) Assert.Empty(db.Database.GetPendingMigrations());

        var tables = Query(_connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
        foreach (var table in LearningTables) Assert.Contains(table, tables);

        var indexes = Query(_connection, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'public'");
        Assert.Contains(indexes, i => i.Contains("IX_muscle_week_observations_UserId_PlanId_Period_Muscle") && i.Contains("UNIQUE"));
        Assert.Contains(indexes, i => i.Contains("IX_longitudinal_decisions_PlanId_MesocycleSequence_Muscle") && i.Contains("UNIQUE"));
        Assert.Contains(indexes, i => i.Contains("IX_frequency_proposals_PlanId ") && i.Contains("UNIQUE") && i.Contains("'pending'"));

        // Every learning table goes with its user and its plan.
        var cascades = Query(_connection,
            "SELECT conrelid::regclass::text || ' -> ' || confrelid::regclass::text FROM pg_constraint WHERE contype = 'f' AND confdeltype = 'c'");
        foreach (var table in LearningTables)
        {
            Assert.Contains($"{table} -> users", cascades);
            Assert.Contains($"{table} -> training_plans", cascades);
        }
    }

    [ScenarioFact]
    public void The_learning_migrations_go_down_and_up_again_without_touching_the_rest()
    {
        using (var db = Context()) db.Database.Migrate();
        using (var db = Context())
        {
            var migrator = db.GetService<IMigrator>();
            migrator.Migrate(BeforeLearning);
        }
        var down = Query(_connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
        foreach (var table in LearningTables) Assert.DoesNotContain(table, down);
        Assert.Contains("training_plans", down);
        Assert.Contains("plan_adaptations", down);

        using (var db = Context()) db.Database.Migrate();
        var up = Query(_connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
        foreach (var table in LearningTables) Assert.Contains(table, up);
    }

    private const string BeforeSegments = "20261206100000_ExercisePlanningMetadata";

    [ScenarioFact]
    public void The_segment_migration_keeps_every_logged_set_as_a_main_set_and_goes_down_and_up_again()
    {
        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeSegments);
        var (user, log) = (Guid.NewGuid(), Guid.NewGuid());
        InsertMinimalRow("users", new() { ["Id"] = $"'{user}'", ["Role"] = "'user'" });
        InsertMinimalRow("workout_logs", new() { ["Id"] = $"'{log}'", ["UserId"] = $"'{user}'", ["PlanId"] = "NULL", ["PlannedSessionId"] = "NULL" });
        InsertMinimalRow("workout_set_logs", new() { ["WorkoutLogId"] = $"'{log}'", ["ExerciseId"] = "'curl'", ["SetNumber"] = "1" });

        using (var db = Context()) db.Database.Migrate();
        using (var db = Context()) Assert.Empty(db.Database.GetPendingMigrations());
        const string Rows = "SELECT concat_ws('|', \"ExerciseId\", \"SetNumber\", \"SegmentIndex\") FROM workout_set_logs ORDER BY \"SegmentIndex\"";
        Assert.Equal(["curl|1|0"], Query(_connection, Rows));

        InsertMinimalRow("workout_set_logs", new() { ["WorkoutLogId"] = $"'{log}'", ["ExerciseId"] = "'curl'", ["SetNumber"] = "1", ["SegmentIndex"] = "1" });
        Assert.ThrowsAny<Exception>(() => InsertMinimalRow("workout_set_logs",
            new() { ["WorkoutLogId"] = $"'{log}'", ["ExerciseId"] = "'curl'", ["SetNumber"] = "1", ["SegmentIndex"] = "1" }));

        // Going down keeps the sets and drops the segments, which the previous version cannot hold.
        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeSegments);
        Assert.Equal("curl", Query(_connection, "SELECT \"ExerciseId\" FROM workout_set_logs").Single());
        using (var db = Context()) db.Database.Migrate();
        Assert.Equal(["curl|1|0"], Query(_connection, Rows));
    }

    private const string BeforeTrace = "20261126100000_FrequencyProposals";
    private const string BeforePrescriptionHistory = "20261127100000_MesocycleGenerationTrace";
    private const string BeforeSessionVersion = "20261128100000_PrescriptionAdaptationHistory";

    /// <summary>Inserts one row giving a neutral value to every NOT NULL column without a default, so the test does not depend on the table's full shape.</summary>
    private void InsertMinimalRow(string table, Dictionary<string, string> values)
    {
        var columns = Query(_connection,
            $"SELECT column_name || '|' || data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' " +
            "AND is_nullable = 'NO' AND column_default IS NULL");
        var names = new List<string>();
        var literals = new List<string>();
        foreach (var column in columns)
        {
            var (name, type) = (column.Split('|')[0], column.Split('|')[1]);
            names.Add($"\"{name}\"");
            literals.Add(values.TryGetValue(name, out var given) ? given : type switch
            {
                "uuid" => "gen_random_uuid()",
                "integer" or "bigint" or "smallint" or "numeric" => "1",
                "boolean" => "false",
                "date" => "current_date",
                var t when t.StartsWith("timestamp") => "now()",
                _ => "'x'"
            });
        }
        Execute(_connection, $"INSERT INTO {table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", literals)})");
    }

    [ScenarioFact]
    public void The_trace_migration_keeps_existing_blocks_untraced_and_goes_down_and_up_again()
    {
        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeTrace);
        var user = Guid.NewGuid();
        var plan = Guid.NewGuid();
        InsertMinimalRow("users", new() { ["Id"] = $"'{user}'", ["Role"] = "'user'" });
        InsertMinimalRow("training_plans", new()
        {
            ["Id"] = $"'{plan}'", ["UserId"] = $"'{user}'", ["Status"] = "'Archived'", ["Origin"] = "'Generated'", ["SchedulingMode"] = "'Calendar'"
        });
        InsertMinimalRow("training_mesocycles", new() { ["PlanId"] = $"'{plan}'", ["Sequence"] = "1" });

        using (var db = Context()) db.Database.Migrate();
        using (var db = Context()) Assert.Empty(db.Database.GetPendingMigrations());

        // The block that existed has no trace and no invented values; the data is still there.
        var row = Query(_connection,
            "SELECT concat_ws('|', \"Sequence\", \"GenerationSnapshotJson\" IS NULL, \"SnapshotSchemaVersion\" IS NULL, \"EngineVersion\" IS NULL, " +
            "\"CatalogFingerprint\" IS NULL, \"GeneratedAt\" IS NULL) FROM training_mesocycles").Single();
        Assert.Equal("1|t|t|t|t|t", row); // PostgreSQL renders a boolean as t/f
        Assert.Contains(Query(_connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'training_mesocycles'::regclass"),
            c => c == "CK_training_mesocycles_trace_complete");

        // A half-written trace is refused by the database.
        Assert.ThrowsAny<Exception>(() => Execute(_connection, "UPDATE training_mesocycles SET \"EngineVersion\" = '0.11.1'"));

        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeTrace);
        Assert.DoesNotContain("EngineVersion", Query(_connection,
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'training_mesocycles'"));
        Assert.Equal("1", Query(_connection, "SELECT \"Sequence\"::text FROM training_mesocycles").Single());
        using (var db = Context()) db.Database.Migrate();
    }

    [ScenarioFact]
    public void The_prescription_history_migration_leaves_existing_sessions_and_adaptations_untouched_and_goes_down_and_up_again()
    {
        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforePrescriptionHistory);
        var (user, plan, block) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        InsertMinimalRow("users", new() { ["Id"] = $"'{user}'", ["Role"] = "'user'" });
        InsertMinimalRow("training_plans", new()
        {
            ["Id"] = $"'{plan}'", ["UserId"] = $"'{user}'", ["Status"] = "'Archived'", ["Origin"] = "'Generated'", ["SchedulingMode"] = "'Calendar'"
        });
        InsertMinimalRow("training_mesocycles", new() { ["Id"] = $"'{block}'", ["PlanId"] = $"'{plan}'", ["Sequence"] = "1" });
        InsertMinimalRow("planned_sessions", new()
        {
            ["PlanId"] = $"'{plan}'", ["MesocycleId"] = $"'{block}'", ["PrescriptionJson"] = "'{\"exercises\":[]}'", ["Status"] = "'Planned'"
        });
        InsertMinimalRow("plan_adaptations", new() { ["PlanId"] = $"'{plan}'", ["Kind"] = "'deload'", ["ExerciseIds"] = "'[]'" });

        using (var db = Context()) db.Database.Migrate();
        using (var db = Context()) Assert.Empty(db.Database.GetPendingMigrations());

        // What existed has no original and no invented trace; the prescription and the audit row are exactly as they were.
        Assert.Equal("{\"exercises\":[]}|t|t|t", Query(_connection,
            "SELECT concat_ws('|', \"PrescriptionJson\", \"OriginalPrescriptionJson\" IS NULL, \"OriginalPrescriptionSchemaVersion\" IS NULL, " +
            "\"PrescriptionSource\" IS NULL) FROM planned_sessions").Single());
        Assert.Equal("deload|t|t|t|t", Query(_connection,
            "SELECT concat_ws('|', \"Kind\", \"Source\" IS NULL, \"EngineVersion\" IS NULL, \"SchemaVersion\" IS NULL, \"ChangesJson\" IS NULL) FROM plan_adaptations").Single());

        // The database refuses half a snapshot, a snapshot that is not the engine's, and changes without their layout version.
        Assert.ThrowsAny<Exception>(() => Execute(_connection, "UPDATE planned_sessions SET \"OriginalPrescriptionJson\" = '{}'"));
        Assert.ThrowsAny<Exception>(() => Execute(_connection,
            "UPDATE planned_sessions SET \"OriginalPrescriptionJson\" = '{}', \"OriginalPrescriptionSchemaVersion\" = 1, \"PrescriptionSource\" = 'user'"));
        Assert.ThrowsAny<Exception>(() => Execute(_connection, "UPDATE plan_adaptations SET \"ChangesJson\" = '{}'"));
        Execute(_connection,
            "UPDATE planned_sessions SET \"OriginalPrescriptionJson\" = '{}', \"OriginalPrescriptionSchemaVersion\" = 1, \"PrescriptionSource\" = 'engine'");

        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforePrescriptionHistory);
        Assert.DoesNotContain("OriginalPrescriptionJson", Query(_connection,
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'planned_sessions'"));
        Assert.DoesNotContain("ChangesJson", Query(_connection,
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'plan_adaptations'"));
        Assert.Equal("{\"exercises\":[]}", Query(_connection, "SELECT \"PrescriptionJson\" FROM planned_sessions").Single());
        Assert.Equal("deload", Query(_connection, "SELECT \"Kind\" FROM plan_adaptations").Single());
        using (var db = Context()) db.Database.Migrate();
    }

    [ScenarioFact]
    public void The_session_version_migration_starts_existing_sessions_at_zero_keeps_a_default_for_older_instances_and_goes_down_and_up_again()
    {
        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeSessionVersion);
        var (user, plan, block) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        InsertMinimalRow("users", new() { ["Id"] = $"'{user}'", ["Role"] = "'user'" });
        InsertMinimalRow("training_plans", new()
        {
            ["Id"] = $"'{plan}'", ["UserId"] = $"'{user}'", ["Status"] = "'Archived'", ["Origin"] = "'Generated'", ["SchedulingMode"] = "'Calendar'"
        });
        InsertMinimalRow("training_mesocycles", new() { ["Id"] = $"'{block}'", ["PlanId"] = $"'{plan}'", ["Sequence"] = "1" });
        InsertMinimalRow("planned_sessions", new()
        {
            ["PlanId"] = $"'{plan}'", ["MesocycleId"] = $"'{block}'", ["PrescriptionJson"] = "'{\"exercises\":[]}'", ["Status"] = "'Planned'"
        });
        Assert.DoesNotContain("Version", Query(_connection, "SELECT column_name FROM information_schema.columns WHERE table_name = 'planned_sessions'"));

        using (var db = Context()) db.Database.Migrate();
        using (var db = Context()) Assert.Empty(db.Database.GetPendingMigrations());

        // The session that existed keeps everything it had and starts at 0, like one created now; the column cannot be null.
        Assert.Equal("{\"exercises\":[]}|0", Query(_connection, "SELECT concat_ws('|', \"PrescriptionJson\", \"Version\") FROM planned_sessions").Single());
        Assert.Equal("NO|0", Query(_connection,
            "SELECT is_nullable || '|' || column_default FROM information_schema.columns WHERE table_name = 'planned_sessions' AND column_name = 'Version'").Single());

        // The real context uses the column as the guard: a stale write is refused and a current one advances it.
        using (var first = Context())
        using (var second = Context())
        {
            var a = first.PlannedSessions.Single();
            var b = second.PlannedSessions.Single();
            Assert.Equal(0, a.Version);
            a.Title = "first";
            first.SaveChanges();
            Assert.Equal(1, a.Version);
            b.Title = "second";
            Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
        }
        Assert.Equal("first|1", Query(_connection, "SELECT concat_ws('|', \"Title\", \"Version\") FROM planned_sessions").Single());

        using (var db = Context()) db.GetService<IMigrator>().Migrate(BeforeSessionVersion);
        Assert.DoesNotContain("Version", Query(_connection, "SELECT column_name FROM information_schema.columns WHERE table_name = 'planned_sessions'"));
        Assert.Equal("first", Query(_connection, "SELECT \"Title\" FROM planned_sessions").Single());
        using (var db = Context()) db.Database.Migrate();
        Assert.Equal("0", Query(_connection, "SELECT \"Version\"::text FROM planned_sessions").Single()); // coming back up starts every session at 0 again
    }
}
