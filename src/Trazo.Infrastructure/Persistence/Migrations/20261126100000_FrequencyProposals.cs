using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Proposals to reorganise the training days of a plan when the user persistently trains a different number of days. A decision never
/// deletes a row, so a reorganisation can be reverted without losing history. Belongs to the user and goes with the account.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261126100000_FrequencyProposals")]
public sealed class FrequencyProposals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE frequency_proposals (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "Kind" character varying(10) NOT NULL,
                "Status" character varying(12) NOT NULL,
                "FromDays" character varying(80) NOT NULL,
                "ToDays" character varying(80) NOT NULL,
                "FromSplit" character varying(20) NULL,
                "ToSplit" character varying(20) NULL,
                "WeeksAnalysed" integer NOT NULL,
                "LowWeeks" integer NOT NULL,
                "Reason" character varying(900) NOT NULL,
                "PreviewJson" character varying(4000) NOT NULL,
                "PolicyVersion" character varying(30) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "DecidedAt" timestamp with time zone NULL,
                "AppliedFromSequence" integer NULL
            );
            CREATE UNIQUE INDEX "IX_frequency_proposals_PlanId"
                ON frequency_proposals ("PlanId") WHERE "Status" = 'pending';
            CREATE INDEX "IX_frequency_proposals_PlanId_CreatedAt"
                ON frequency_proposals ("PlanId", "CreatedAt");
            CREATE INDEX "IX_frequency_proposals_UserId_CreatedAt"
                ON frequency_proposals ("UserId", "CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS frequency_proposals;");
    }
}
