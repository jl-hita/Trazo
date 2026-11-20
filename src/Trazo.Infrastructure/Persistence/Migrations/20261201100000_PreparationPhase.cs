using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The starting point a user declares when the plan is created (Regular, New or Returning) and the weeks of preparation it gives the plan.
/// Purely additive: two NOT NULL columns with constant defaults, which PostgreSQL adds without rewriting the table. Every plan that exists
/// is a regular one with no preparation, exactly like a plan created now without the choice. The defaults stay in the database so an
/// instance of the previous version, still running during a rolling deploy, can keep inserting plans.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261201100000_PreparationPhase")]
public sealed class PreparationPhase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans ADD COLUMN IF NOT EXISTS "Readiness" character varying(20) NOT NULL DEFAULT 'Regular';
            ALTER TABLE training_plans ADD COLUMN IF NOT EXISTS "PreparationWeeks" integer NOT NULL DEFAULT 0;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "PreparationWeeks";
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "Readiness";
        """);
    }
}
