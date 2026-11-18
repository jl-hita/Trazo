using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Keeps the database role constraint in sync with the roles exposed by the admin API.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261118193000_ExpandSupportedUserRoles")]
public sealed class ExpandSupportedUserRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE users DROP CONSTRAINT IF EXISTS "CK_users_role";
            ALTER TABLE users
                ADD CONSTRAINT "CK_users_role"
                CHECK ("Role" IN ('user', 'admin', 'accountant', 'commercial', 'superadmin'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE users DROP CONSTRAINT IF EXISTS "CK_users_role";
            ALTER TABLE users
                ADD CONSTRAINT "CK_users_role"
                CHECK ("Role" IN ('user', 'admin', 'superadmin'));
            """);
    }
}
