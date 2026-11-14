using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The user's IANA time zone, used to decide which day and week it is for them. Null means the default (Europe/Madrid),
/// so existing users need no data change. Account deletion needs no schema change: every table with user data already
/// references <c>users</c> with ON DELETE CASCADE (see docs/account-deletion.md).
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114140000_UserTimeZone")]
public sealed class UserTimeZone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users ADD COLUMN IF NOT EXISTS "TimeZoneId" character varying(64) NULL;""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users DROP COLUMN IF EXISTS "TimeZoneId";""");
    }
}
