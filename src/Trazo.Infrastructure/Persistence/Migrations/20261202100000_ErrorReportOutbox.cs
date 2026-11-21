using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Outbox of the error-ticket system: a report is written here before it is turned into a ticket and removed once the ticket is stored,
/// so a restart or a failure of the processor does not lose it. Purely additive (a new table), so a previous version that is still
/// running during a rolling deploy is not affected.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261202100000_ErrorReportOutbox")]
public sealed class ErrorReportOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE error_report_outbox (
                "Id" uuid PRIMARY KEY,
                "Source" character varying(16) NOT NULL,
                "AppVersion" character varying(40) NULL,
                "StackTrace" character varying(40000) NOT NULL,
                "TraceId" character varying(64) NULL,
                "ReportedAt" timestamp with time zone NOT NULL,
                "Attempts" integer NOT NULL,
                "NextAttemptAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_error_report_outbox_NextAttemptAt" ON error_report_outbox ("NextAttemptAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS error_report_outbox;");
    }
}
