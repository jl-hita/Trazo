using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Registration tokens of the phones that receive push notifications. They go away with the account.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261115130000_PushNotifications")]
public sealed class PushNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE device_tokens (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Token" character varying(1024) NOT NULL,
                "Platform" character varying(20) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_device_tokens_Token" ON device_tokens ("Token");
            CREATE INDEX "IX_device_tokens_UserId" ON device_tokens ("UserId");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS device_tokens;");
    }
}
