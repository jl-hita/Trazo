using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Conversion metrics: when a subscription was first paid, and an anonymous daily counter of Premium offer views.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261115140000_ConversionMetrics")]
public sealed class ConversionMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE subscriptions ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone NULL;
            -- Rows already paid before this column existed.
            UPDATE subscriptions SET "PaidAt" = "StartedAt" WHERE "PaidAt" IS NULL AND "Status" IN ('Active', 'GracePeriod', 'OnHold');
            CREATE TABLE paywall_views (
                "Day" date NOT NULL,
                "Source" character varying(16) NOT NULL,
                "Views" integer NOT NULL,
                PRIMARY KEY ("Day", "Source")
            );
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS paywall_views;
            ALTER TABLE subscriptions DROP COLUMN IF EXISTS "PaidAt";
        """);
    }
}
