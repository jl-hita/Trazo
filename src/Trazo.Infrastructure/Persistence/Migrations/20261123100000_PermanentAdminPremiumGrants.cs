using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Adds explicit permanence and a durable reason to manual Premium grant ledger entries.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261123100000_PermanentAdminPremiumGrants")]
public sealed class PermanentAdminPremiumGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE premium_grants DROP CONSTRAINT IF EXISTS "CK_premium_grants_Days";
            ALTER TABLE premium_grants ADD COLUMN "IsPermanent" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE premium_grants ADD COLUMN "GrantReason" character varying(300) NULL;
            ALTER TABLE premium_grants ADD COLUMN "GrantedBy" character varying(40) NULL;
            ALTER TABLE premium_grants ADD CONSTRAINT "CK_premium_grants_Days"
                CHECK (("IsPermanent" = TRUE AND "Days" = 0 AND "Kind" = 'admin_grant') OR ("IsPermanent" = FALSE AND "Days" > 0));
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE premium_grants DROP CONSTRAINT IF EXISTS "CK_premium_grants_Days";
            UPDATE premium_grants SET "Days" = 1, "EndsAt" = "StartsAt" + INTERVAL '1 day' WHERE "IsPermanent" = TRUE;
            ALTER TABLE premium_grants DROP COLUMN IF EXISTS "GrantedBy";
            ALTER TABLE premium_grants DROP COLUMN IF EXISTS "GrantReason";
            ALTER TABLE premium_grants DROP COLUMN IF EXISTS "IsPermanent";
            ALTER TABLE premium_grants ADD CONSTRAINT "CK_premium_grants_Days" CHECK ("Days" > 0);
        """);
    }
}
