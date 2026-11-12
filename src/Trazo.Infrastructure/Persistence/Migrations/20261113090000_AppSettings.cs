using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261113090000_AppSettings")]
public sealed class AppSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE app_settings (
                "Key" character varying(100) PRIMARY KEY,
                "Value" character varying(4000) NOT NULL,
                "IsSecret" boolean NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS app_settings;");
    }
}
