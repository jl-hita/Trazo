using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Comments sent from the app (docs/feedback.md). Purely additive (a new table), so a previous version that is still running during a
/// rolling deploy is not affected.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261211100000_TesterFeedback")]
public sealed class TesterFeedback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE feedback_entries (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "Category" character varying(12) NOT NULL,
                "Message" character varying(2000) NOT NULL,
                "Screen" character varying(60) NULL,
                "AppVersion" character varying(40) NULL,
                "Device" character varying(120) NULL,
                "Language" character varying(12) NULL,
                "Status" character varying(12) NOT NULL,
                "ClientRequestId" uuid NOT NULL
            );
            CREATE UNIQUE INDEX "IX_feedback_entries_UserId_ClientRequestId" ON feedback_entries ("UserId", "ClientRequestId");
            CREATE INDEX "IX_feedback_entries_Status_CreatedAt" ON feedback_entries ("Status", "CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS feedback_entries;");
    }
}
