using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Separates planning from execution. Every workout log gets the time it really happened (<c>PerformedAt</c>, local day and zone)
/// next to the time it was registered (<c>LoggedAt</c>); every planned session gets its position in the program
/// (<c>SequenceIndex</c>); adaptations remember the time of the workout that caused them. Existing rows are back-filled:
/// <c>PerformedAt = LoggedAt</c> (the best known value, nothing is lost), the local day uses the default zone, and the sequence
/// follows the planned date. No session or log changes identity.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261117100000_WorkoutPerformedAtAndSessionSequence")]
public sealed class WorkoutPerformedAtAndSessionSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE workout_logs ADD COLUMN "PerformedAt" timestamp with time zone NULL;
            ALTER TABLE workout_logs ADD COLUMN "PerformedOn" date NULL;
            ALTER TABLE workout_logs ADD COLUMN "TimeZoneId" character varying(64) NOT NULL DEFAULT 'Europe/Madrid';
            ALTER TABLE workout_logs ADD COLUMN "ClientRequestId" uuid NULL;
            UPDATE workout_logs SET "PerformedAt" = "LoggedAt", "PerformedOn" = ("LoggedAt" AT TIME ZONE 'Europe/Madrid')::date;
            ALTER TABLE workout_logs ALTER COLUMN "PerformedAt" SET NOT NULL;
            ALTER TABLE workout_logs ALTER COLUMN "PerformedOn" SET NOT NULL;
            ALTER TABLE workout_logs ALTER COLUMN "TimeZoneId" DROP DEFAULT;
            CREATE INDEX "IX_workout_logs_PlanId_PerformedAt" ON workout_logs ("PlanId", "PerformedAt");
            DROP INDEX IF EXISTS "IX_workout_logs_UserId";
            CREATE UNIQUE INDEX "IX_workout_logs_UserId_ClientRequestId" ON workout_logs ("UserId", "ClientRequestId") WHERE "ClientRequestId" IS NOT NULL;

            ALTER TABLE planned_sessions ADD COLUMN "SequenceIndex" integer NOT NULL DEFAULT 0;
            UPDATE planned_sessions s SET "SequenceIndex" = r.n
            FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "PlanId" ORDER BY "Date", "Title", "Id") AS n FROM planned_sessions) r
            WHERE s."Id" = r."Id";
            ALTER TABLE planned_sessions ALTER COLUMN "SequenceIndex" DROP DEFAULT;
            CREATE UNIQUE INDEX "IX_planned_sessions_PlanId_SequenceIndex" ON planned_sessions ("PlanId", "SequenceIndex");

            ALTER TABLE plan_adaptations ADD COLUMN "EvidenceAt" timestamp with time zone NULL;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE plan_adaptations DROP COLUMN IF EXISTS "EvidenceAt";
            DROP INDEX IF EXISTS "IX_planned_sessions_PlanId_SequenceIndex";
            ALTER TABLE planned_sessions DROP COLUMN IF EXISTS "SequenceIndex";
            DROP INDEX IF EXISTS "IX_workout_logs_UserId_ClientRequestId";
            CREATE INDEX IF NOT EXISTS "IX_workout_logs_UserId" ON workout_logs ("UserId");
            DROP INDEX IF EXISTS "IX_workout_logs_PlanId_PerformedAt";
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "ClientRequestId";
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "TimeZoneId";
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "PerformedOn";
            ALTER TABLE workout_logs DROP COLUMN IF EXISTS "PerformedAt";
        """);
    }
}
