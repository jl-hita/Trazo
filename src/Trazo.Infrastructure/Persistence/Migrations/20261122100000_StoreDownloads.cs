using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Weekly downloads from Google Play Console, entered by the marketing team (no user data).</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261122100000_StoreDownloads")]
public sealed class StoreDownloads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE store_downloads (
                "Week" date PRIMARY KEY,
                "Downloads" integer NOT NULL,
                "Note" character varying(200) NULL,
                "UpdatedBy" character varying(60) NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS store_downloads;");
    }
}
