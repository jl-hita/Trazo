using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Application logs shown in the administration console. Written with plain SQL by the database logger (not through
/// the EF model, like the exercise images), so the model snapshot is unchanged.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261113140000_AppLogs")]
public sealed class AppLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE app_logs (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                timestamp_utc timestamp with time zone NOT NULL,
                level smallint NOT NULL,
                category character varying(200) NOT NULL,
                message text NOT NULL,
                exception text NULL,
                trace_id character varying(40) NULL
            );
            CREATE INDEX ix_app_logs_timestamp ON app_logs (timestamp_utc DESC, id DESC);
            CREATE INDEX ix_app_logs_level_timestamp ON app_logs (level, timestamp_utc DESC);
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS app_logs;");
    }
}
