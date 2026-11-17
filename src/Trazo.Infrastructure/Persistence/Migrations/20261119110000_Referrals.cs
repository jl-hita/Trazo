using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Referral codes and relationships. The reward itself is a row of premium_grants (kind referral_reward, source key = the relationship id).</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261119110000_Referrals")]
public sealed class Referrals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE premium_grants ADD COLUMN "GoogleOrderBefore" character varying(100) NULL;

            CREATE TABLE referral_codes (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Code" character varying(12) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_referral_codes_Code" ON referral_codes ("Code");
            CREATE UNIQUE INDEX "IX_referral_codes_UserId" ON referral_codes ("UserId");

            CREATE TABLE referral_relationships (
                "Id" uuid PRIMARY KEY,
                "ReferrerUserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "ReferredUserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Code" character varying(12) NOT NULL,
                "Source" character varying(20) NOT NULL,
                "Status" character varying(16) NOT NULL,
                "AttributedAt" timestamp with time zone NOT NULL,
                "AppVersion" character varying(40) NULL,
                "ConversionSubscriptionId" uuid NULL,
                "ConversionPlan" character varying(200) NULL,
                "ConvertedAt" timestamp with time zone NULL,
                "RewardGrantId" uuid NULL,
                "RewardDays" integer NULL,
                "Incident" character varying(40) NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "CK_referral_relationships_not_self" CHECK ("ReferrerUserId" <> "ReferredUserId")
            );
            CREATE UNIQUE INDEX "IX_referral_relationships_ReferredUserId" ON referral_relationships ("ReferredUserId");
            CREATE INDEX "IX_referral_relationships_ReferrerUserId_Status" ON referral_relationships ("ReferrerUserId", "Status");
            CREATE INDEX "IX_referral_relationships_Status" ON referral_relationships ("Status");
            CREATE INDEX "IX_referral_relationships_Code" ON referral_relationships ("Code");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS referral_relationships;
            DROP TABLE IF EXISTS referral_codes;
            ALTER TABLE premium_grants DROP COLUMN IF EXISTS "GoogleOrderBefore";
        """);
    }
}
