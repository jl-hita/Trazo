using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Waitlist of the public landing page: one row per address, with the consent date.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261115100000_Waitlist")]
public sealed class Waitlist : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE waitlist_entries (
                "Id" uuid PRIMARY KEY,
                "Email" character varying(254) NOT NULL,
                "Language" character varying(2) NOT NULL,
                "ConsentAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_waitlist_entries_Email" ON waitlist_entries ("Email");
            CREATE INDEX "IX_waitlist_entries_CreatedAt" ON waitlist_entries ("CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS waitlist_entries;");
    }
}
