using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Store subscriptions, and room in app_settings for an encrypted Google service-account key.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114100000_Subscriptions")]
public sealed class Subscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE app_settings ALTER COLUMN "Value" TYPE character varying(16000);""");
        migrationBuilder.Sql("""
            CREATE TABLE subscriptions (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Source" character varying(30) NOT NULL,
                "ProductId" character varying(200) NOT NULL,
                "PurchaseTokenHash" character varying(64) NOT NULL,
                "ProtectedPurchaseToken" character varying(4000) NOT NULL,
                "Status" character varying(20) NOT NULL,
                "StoreState" character varying(60) NULL,
                "LatestOrderId" character varying(100) NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "AutoRenewing" boolean NOT NULL,
                "AcknowledgedAt" timestamp with time zone NULL,
                "IsTestPurchase" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_subscriptions_PurchaseTokenHash" ON subscriptions ("PurchaseTokenHash");
            CREATE INDEX "IX_subscriptions_UserId" ON subscriptions ("UserId");
            CREATE INDEX "IX_subscriptions_Status_ExpiresAt" ON subscriptions ("Status", "ExpiresAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS subscriptions;");
        migrationBuilder.Sql("""ALTER TABLE app_settings ALTER COLUMN "Value" TYPE character varying(4000);""");
    }
}
