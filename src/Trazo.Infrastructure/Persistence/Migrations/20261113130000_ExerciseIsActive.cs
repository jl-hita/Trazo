using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Lets an administrator hide an exercise from the catalog without deleting it. The database default keeps
/// every existing row, and every row inserted by the DietoExpress importer, active.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261113130000_ExerciseIsActive")]
public sealed class ExerciseIsActive : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE exercise_catalog DROP COLUMN IF EXISTS "IsActive";""");
    }
}
