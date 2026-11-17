using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>A calendar plan can be moved forward from today; the days it was moved are remembered so the past stays open for late registrations.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261118100000_PlanScheduleShift")]
public sealed class PlanScheduleShift : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans ADD COLUMN "ScheduleShiftDays" integer NOT NULL DEFAULT 0;
            ALTER TABLE training_plans ALTER COLUMN "ScheduleShiftDays" DROP DEFAULT;
            CREATE UNIQUE INDEX "IX_plan_adaptations_PlanId_EvidenceAt" ON plan_adaptations ("PlanId", "EvidenceAt") WHERE "Kind" = 'reentry';
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_plan_adaptations_PlanId_EvidenceAt";
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "ScheduleShiftDays";
        """);
    }
}
