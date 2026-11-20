using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Optimistic concurrency for planned sessions: a counter that EF compares in every UPDATE and DELETE of a session and that the context advances
/// on every UPDATE, so a stale write fails instead of overwriting the one that won. Purely additive: one NOT NULL column with a constant default,
/// which PostgreSQL adds without rewriting the table; every session that already exists starts at 0, exactly like a session created now.
/// The default stays in the database so an instance of the previous version, still running during a rolling deploy, can keep inserting sessions.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261129100000_PlannedSessionVersion")]
public sealed class PlannedSessionVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE planned_sessions ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE planned_sessions DROP COLUMN IF EXISTS "Version";
        """);
    }
}
