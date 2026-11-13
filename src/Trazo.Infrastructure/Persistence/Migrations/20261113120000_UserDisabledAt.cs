using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Lets an administrator disable an account without deleting it.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261113120000_UserDisabledAt")]
public sealed class UserDisabledAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users ADD COLUMN IF NOT EXISTS "DisabledAt" timestamp with time zone NULL;""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users DROP COLUMN IF EXISTS "DisabledAt";""");
    }
}
