using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Marketing campaigns with tracking links, the campaign each new account came from, anonymous daily link counters and spend entries.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261121100000_MarketingCampaigns")]
public sealed class MarketingCampaigns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE marketing_campaigns (
                "Id" uuid PRIMARY KEY,
                "Slug" character varying(32) NOT NULL,
                "Name" character varying(80) NOT NULL,
                "Channel" character varying(20) NOT NULL,
                "Status" character varying(12) NOT NULL,
                "StartsOn" date NULL,
                "EndsOn" date NULL,
                "Notes" character varying(500) NULL,
                "CreatedBy" character varying(60) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_marketing_campaigns_Slug" ON marketing_campaigns ("Slug");

            CREATE TABLE campaign_attributions (
                "UserId" uuid PRIMARY KEY REFERENCES users ("Id") ON DELETE CASCADE,
                "CampaignId" uuid NOT NULL REFERENCES marketing_campaigns ("Id") ON DELETE RESTRICT,
                "Source" character varying(20) NOT NULL,
                "AttributedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_campaign_attributions_CampaignId" ON campaign_attributions ("CampaignId");

            CREATE TABLE campaign_daily_stats (
                "CampaignId" uuid NOT NULL REFERENCES marketing_campaigns ("Id") ON DELETE CASCADE,
                "Day" date NOT NULL,
                "Clicks" integer NOT NULL,
                "Installs" integer NOT NULL,
                PRIMARY KEY ("CampaignId", "Day")
            );

            CREATE TABLE campaign_spend (
                "Id" uuid PRIMARY KEY,
                "CampaignId" uuid NOT NULL REFERENCES marketing_campaigns ("Id") ON DELETE CASCADE,
                "Day" date NOT NULL,
                "Amount" numeric(12,2) NOT NULL,
                "Note" character varying(200) NULL,
                "CreatedBy" character varying(60) NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_campaign_spend_CampaignId_Day" ON campaign_spend ("CampaignId", "Day");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS campaign_spend;
            DROP TABLE IF EXISTS campaign_daily_stats;
            DROP TABLE IF EXISTS campaign_attributions;
            DROP TABLE IF EXISTS marketing_campaigns;
        """);
    }
}
