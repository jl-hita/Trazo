using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261112170000_InitialExerciseCatalog")]
public sealed class InitialExerciseCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "exercise_catalog",
            columns: table => new
            {
                exercise_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                NameEs = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                DescriptionEs = table.Column<string>(type: "text", nullable: false),
                InstructionsEs = table.Column<string[]>(type: "text[]", nullable: false),
                TipsEs = table.Column<string[]>(type: "text[]", nullable: false),
                ForceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Mechanic = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Difficulty = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Equipment = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                BodyPart = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Met = table.Column<double>(type: "double precision", nullable: false),
                IsUnilateral = table.Column<bool>(type: "boolean", nullable: false),
                IsBodyweight = table.Column<bool>(type: "boolean", nullable: false),
                MovementPattern = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PrimaryMuscles = table.Column<string[]>(type: "text[]", nullable: false),
                SecondaryMuscles = table.Column<string[]>(type: "text[]", nullable: false),
                MinimumExperience = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SuitableGoals = table.Column<string[]>(type: "text[]", nullable: false),
                RequiredEquipment = table.Column<string[]>(type: "text[]", nullable: false),
                SelectionPriority = table.Column<int>(type: "integer", nullable: false),
                SeedVersion = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_exercise_catalog", x => x.exercise_id));

        migrationBuilder.CreateIndex(
            name: "IX_exercise_catalog_Source",
            table: "exercise_catalog",
            column: "Source");
        migrationBuilder.CreateIndex(
            name: "IX_exercise_catalog_MovementPattern",
            table: "exercise_catalog",
            column: "MovementPattern");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "exercise_catalog");
    }
}
