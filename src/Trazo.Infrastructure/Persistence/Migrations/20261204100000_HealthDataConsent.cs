using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// When the user gave the explicit consent for health data, and when it was last withdrawn. Purely additive, so a previous version that is
/// still running during a rolling deploy is not affected.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261204100000_HealthDataConsent")]
public sealed class HealthDataConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE users ADD COLUMN "HealthConsentAt" timestamp with time zone NULL;
        ALTER TABLE users ADD COLUMN "HealthConsentWithdrawnAt" timestamp with time zone NULL;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE users DROP COLUMN "HealthConsentAt";
        ALTER TABLE users DROP COLUMN "HealthConsentWithdrawnAt";
    """);
}
