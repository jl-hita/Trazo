using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Anonymous error tickets (API exceptions and Android crashes), one row per distinct error.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261115120000_ErrorTickets")]
public sealed class ErrorTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE error_tickets (
                "Id" uuid PRIMARY KEY,
                "Fingerprint" character varying(64) NOT NULL,
                "Source" character varying(16) NOT NULL,
                "AppVersion" character varying(40) NULL,
                "ErrorType" character varying(200) NOT NULL,
                "StackTrace" character varying(16000) NOT NULL,
                "Occurrences" integer NOT NULL,
                "FirstSeenAt" timestamp with time zone NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL,
                "Status" character varying(16) NOT NULL
            );
            CREATE UNIQUE INDEX "IX_error_tickets_Fingerprint" ON error_tickets ("Fingerprint");
            CREATE INDEX "IX_error_tickets_LastSeenAt" ON error_tickets ("LastSeenAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS error_tickets;");
    }
}
