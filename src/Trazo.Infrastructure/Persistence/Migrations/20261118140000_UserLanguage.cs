using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>The language a user chose for the texts of the API, e-mails and notifications (null = automatic: the language of the request).</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261118140000_UserLanguage")]
public sealed class UserLanguage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users ADD COLUMN "Language" character varying(5) NULL;""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users DROP COLUMN IF EXISTS "Language";""");
    }
}
