using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Plans can advance by the order of their sessions instead of by calendar weeks. A plan gets a scheduling mode (existing plans are
/// all <c>Calendar</c>) and the sessions of one period; phases get their period range (back-filled from their dates: week 1 starts
/// at the plan's start date) and their dates, like the dates of a planned session, may be empty in a sequence plan.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261117150000_AsynchronousSplit")]
public sealed class AsynchronousSplit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_plans ADD COLUMN "SchedulingMode" character varying(20) NOT NULL DEFAULT 'Calendar';
            ALTER TABLE training_plans ADD COLUMN "SessionsPerPeriod" integer NOT NULL DEFAULT 0;
            ALTER TABLE training_plans ALTER COLUMN "SchedulingMode" DROP DEFAULT;
            ALTER TABLE training_plans ALTER COLUMN "SessionsPerPeriod" DROP DEFAULT;
            UPDATE training_plans SET "SessionsPerPeriod" = COALESCE(jsonb_array_length("TrainingDays"::jsonb), 0);

            ALTER TABLE training_plan_phases ADD COLUMN "StartPeriod" integer NOT NULL DEFAULT 0;
            ALTER TABLE training_plan_phases ADD COLUMN "EndPeriod" integer NOT NULL DEFAULT 0;
            UPDATE training_plan_phases f SET
                "StartPeriod" = (f."StartDate" - p."StartDate") / 7 + 1,
                "EndPeriod" = (f."EndDate" - p."StartDate") / 7 + 1
            FROM training_plans p WHERE p."Id" = f."PlanId";
            ALTER TABLE training_plan_phases ALTER COLUMN "StartPeriod" DROP DEFAULT;
            ALTER TABLE training_plan_phases ALTER COLUMN "EndPeriod" DROP DEFAULT;
            ALTER TABLE training_plan_phases ALTER COLUMN "StartDate" DROP NOT NULL;
            ALTER TABLE training_plan_phases ALTER COLUMN "EndDate" DROP NOT NULL;

            ALTER TABLE planned_sessions ALTER COLUMN "Date" DROP NOT NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM training_plans WHERE "SchedulingMode" = 'Sequence';
            ALTER TABLE planned_sessions ALTER COLUMN "Date" SET NOT NULL;
            ALTER TABLE training_plan_phases ALTER COLUMN "EndDate" SET NOT NULL;
            ALTER TABLE training_plan_phases ALTER COLUMN "StartDate" SET NOT NULL;
            ALTER TABLE training_plan_phases DROP COLUMN IF EXISTS "EndPeriod";
            ALTER TABLE training_plan_phases DROP COLUMN IF EXISTS "StartPeriod";
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "SessionsPerPeriod";
            ALTER TABLE training_plans DROP COLUMN IF EXISTS "SchedulingMode";
        """);
    }
}
