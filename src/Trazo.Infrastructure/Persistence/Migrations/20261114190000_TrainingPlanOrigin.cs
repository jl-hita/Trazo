using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Records whether a plan was created manually or by the automatic generator.
/// Existing plans are generated because the current API only creates generated plans.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114190000_TrainingPlanOrigin")]
public sealed class TrainingPlanOrigin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans
                ADD COLUMN "Origin" character varying(20) NOT NULL DEFAULT 'Generated';
            ALTER TABLE training_plans ALTER COLUMN "Origin" DROP DEFAULT;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans DROP COLUMN "Origin";
        """);
    }
}
