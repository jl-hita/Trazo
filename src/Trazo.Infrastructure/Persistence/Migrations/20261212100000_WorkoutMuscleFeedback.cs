using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The answers about each muscle of a workout. Purely additive (a nullable column), so a previous version that is still running during a
/// rolling deploy is not affected.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261212100000_WorkoutMuscleFeedback")]
public sealed class WorkoutMuscleFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE workout_logs ADD COLUMN IF NOT EXISTS "MuscleFeedbackJson" character varying(2000) NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "MuscleFeedbackJson";
        """);
    }
}
