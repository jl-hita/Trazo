using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Post-exercise answers (stimulus, discomfort, pre-fatigue) saved with each workout log.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261116210000_WorkoutExerciseFeedback")]
public sealed class WorkoutExerciseFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE workout_logs ADD COLUMN "ExerciseFeedbackJson" character varying(4000) NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "ExerciseFeedbackJson";
        """);
    }
}
