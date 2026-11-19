using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>A plan remembers which muscles the user wants to emphasize or only maintain ("Muscle:Priority" entries, as JSON text).</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261124100000_PlanMusclePriorities")]
public sealed class PlanMusclePriorities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans ADD COLUMN "MusclePriorities" text NOT NULL DEFAULT '[]';
            ALTER TABLE training_plans ALTER COLUMN "MusclePriorities" DROP DEFAULT;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE training_plans DROP COLUMN IF EXISTS "MusclePriorities";""");
    }
}
