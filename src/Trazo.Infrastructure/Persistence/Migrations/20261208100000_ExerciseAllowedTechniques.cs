using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The set techniques each exercise admits. Existing rows admit none; the exercise seed, applied right after the migrations,
/// fills its exercises.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261208100000_ExerciseAllowedTechniques")]
public sealed class ExerciseAllowedTechniques : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE exercise_catalog ADD COLUMN "AllowedTechniques" text[] NOT NULL DEFAULT '{}';
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE exercise_catalog DROP COLUMN "AllowedTechniques";
    """);
}
