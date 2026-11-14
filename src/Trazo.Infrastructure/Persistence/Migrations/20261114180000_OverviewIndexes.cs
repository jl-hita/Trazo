using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Indexes for the "last 7 days" counts of the administration overview (new users, new plans, logged workouts), so those
/// counts do not scan the tables as they grow. Created with plain SQL like the log indexes, so the model snapshot is unchanged.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114180000_OverviewIndexes")]
public sealed class OverviewIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS ix_users_created_at ON users ("CreatedAt");
            CREATE INDEX IF NOT EXISTS ix_training_plans_created_at ON training_plans ("CreatedAt");
            CREATE INDEX IF NOT EXISTS ix_workout_logs_logged_at ON workout_logs ("LoggedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_workout_logs_logged_at;
            DROP INDEX IF EXISTS ix_training_plans_created_at;
            DROP INDEX IF EXISTS ix_users_created_at;
        """);
    }
}
