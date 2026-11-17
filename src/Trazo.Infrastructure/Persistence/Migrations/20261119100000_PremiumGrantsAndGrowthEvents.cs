using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Premium-time ledger (welcome period, referral rewards, manual grants) and the funnel event log. Neither touches the
/// Google Play subscriptions table. Growth events keep no user once the account is deleted (ON DELETE SET NULL).
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261119100000_PremiumGrantsAndGrowthEvents")]
public sealed class PremiumGrantsAndGrowthEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE premium_grants (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Kind" character varying(30) NOT NULL,
                "SourceKey" character varying(80) NOT NULL,
                "Days" integer NOT NULL,
                "Status" character varying(16) NOT NULL,
                "GrantedAt" timestamp with time zone NOT NULL,
                "StartsAt" timestamp with time zone NULL,
                "EndsAt" timestamp with time zone NULL,
                "OriginUserId" uuid NULL,
                "OriginSubscriptionId" uuid NULL,
                "DeferredSubscriptionId" uuid NULL,
                "GoogleExpiryBefore" timestamp with time zone NULL,
                "GoogleExpiryAfter" timestamp with time zone NULL,
                "AppliedAt" timestamp with time zone NULL,
                "VoidedAt" timestamp with time zone NULL,
                "VoidReason" character varying(300) NULL,
                "VoidedBy" character varying(40) NULL,
                "DeferAttempts" integer NOT NULL,
                "LastDeferError" character varying(300) NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "CK_premium_grants_Days" CHECK ("Days" > 0)
            );
            CREATE UNIQUE INDEX "IX_premium_grants_Kind_SourceKey" ON premium_grants ("Kind", "SourceKey");
            CREATE INDEX "IX_premium_grants_UserId_Status" ON premium_grants ("UserId", "Status");
            CREATE INDEX "IX_premium_grants_Status" ON premium_grants ("Status");

            CREATE TABLE growth_events (
                "Id" uuid PRIMARY KEY,
                "Name" character varying(40) NOT NULL,
                "OccurredAt" timestamp with time zone NOT NULL,
                "UserId" uuid NULL REFERENCES users ("Id") ON DELETE SET NULL,
                "Channel" character varying(30) NULL,
                "Reference" character varying(40) NULL,
                "Plan" character varying(200) NULL,
                "AppVersion" character varying(40) NULL,
                "Variant" character varying(40) NULL,
                "DedupeKey" character varying(120) NULL
            );
            CREATE UNIQUE INDEX "IX_growth_events_DedupeKey" ON growth_events ("DedupeKey") WHERE "DedupeKey" IS NOT NULL;
            CREATE INDEX "IX_growth_events_Name_OccurredAt" ON growth_events ("Name", "OccurredAt");
            CREATE INDEX "IX_growth_events_UserId" ON growth_events ("UserId");
            CREATE INDEX "IX_growth_events_Reference_Name" ON growth_events ("Reference", "Name");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS growth_events;
            DROP TABLE IF EXISTS premium_grants;
        """);
    }
}
