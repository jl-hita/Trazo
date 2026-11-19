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
}
