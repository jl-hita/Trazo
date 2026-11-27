using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Why a plan was archived (docs/research-consent.md). Purely additive: one nullable column, so plans already archived have no reason and
/// an instance of the previous version, still running during a rolling deploy, keeps working.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261210100000_PlanArchiveReason")]
public sealed class PlanArchiveReasonColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans ADD COLUMN IF NOT EXISTS "ArchiveReason" character varying(30) NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "ArchiveReason";
        """);
    }
}
