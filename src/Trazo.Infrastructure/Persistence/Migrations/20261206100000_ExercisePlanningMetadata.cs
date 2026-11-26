using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The planning metadata that used to be deduced from exercise ids. Existing rows start empty and unreviewed; the exercise seed,
/// applied right after the migrations, fills its exercises. The defaults also cover rows written by SQL that does not name them.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261206100000_ExercisePlanningMetadata")]
public sealed class ExercisePlanningMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE exercise_catalog
            ADD COLUMN "EffectiveSecondaryMuscles" text[] NOT NULL DEFAULT '{}',
            ADD COLUMN "Emphasis" text[] NOT NULL DEFAULT '{}',
            ADD COLUMN "LimitationAreas" text[] NOT NULL DEFAULT '{}',
            ADD COLUMN "MetadataReviewed" boolean NOT NULL DEFAULT FALSE;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE exercise_catalog
            DROP COLUMN "EffectiveSecondaryMuscles", DROP COLUMN "Emphasis",
            DROP COLUMN "LimitationAreas", DROP COLUMN "MetadataReviewed";
    """);
}
