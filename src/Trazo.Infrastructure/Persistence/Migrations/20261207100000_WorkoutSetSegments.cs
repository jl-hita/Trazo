using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Segments of a logged set. Every existing row is a main set (segment 0), so nothing is rewritten. Going down removes the
/// segments, which the previous version cannot hold.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261207100000_WorkoutSetSegments")]
public sealed class WorkoutSetSegments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE workout_set_logs ADD COLUMN "SegmentIndex" integer NOT NULL DEFAULT 0;
        CREATE UNIQUE INDEX ux_workout_set_logs_set_segment ON workout_set_logs ("WorkoutLogId", "ExerciseId", "SetNumber", "SegmentIndex");
        DROP INDEX "IX_workout_set_logs_WorkoutLogId_ExerciseId_SetNumber";
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM workout_set_logs WHERE "SegmentIndex" > 0;
        CREATE UNIQUE INDEX "IX_workout_set_logs_WorkoutLogId_ExerciseId_SetNumber" ON workout_set_logs ("WorkoutLogId", "ExerciseId", "SetNumber");
        DROP INDEX ux_workout_set_logs_set_segment;
        ALTER TABLE workout_set_logs DROP COLUMN "SegmentIndex";
    """);
}
