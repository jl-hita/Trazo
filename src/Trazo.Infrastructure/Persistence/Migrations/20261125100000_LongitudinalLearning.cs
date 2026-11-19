using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Longitudinal learning: one observation per user, plan, period and muscle (rebuilt, never added to, so it is idempotent) and an audit of
/// every volume decision taken when a block is generated. Both belong to the user and go with the account.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261125100000_LongitudinalLearning")]
public sealed class LongitudinalLearning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE muscle_week_observations (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "Period" integer NOT NULL,
                "MesocycleSequence" integer NOT NULL,
                "IsDeload" boolean NOT NULL,
                "Muscle" character varying(30) NOT NULL,
                "Quality" character varying(12) NOT NULL,
                "Reason" character varying(30) NULL,
                "PrescribedSets" numeric(6,1) NOT NULL,
                "PerformedSets" numeric(6,1) NOT NULL,
                "SessionsPlanned" integer NOT NULL,
                "SessionsDone" integer NOT NULL,
                "Exposures" integer NOT NULL,
                "CompletedSets" integer NOT NULL,
                "BelowRangeSets" integer NOT NULL,
                "PerformanceChangePercent" numeric(8,2) NULL,
                "PerformanceSamples" integer NOT NULL,
                "FatigueSignals" integer NOT NULL,
                "FeedbackAnswers" integer NOT NULL,
                "LowStimulusAnswers" integer NOT NULL,
                "DiscomfortAnswers" integer NOT NULL,
                "PainSessions" integer NOT NULL,
                "ReturnFromAbsence" boolean NOT NULL,
                "ProcessedThrough" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_muscle_week_observations_UserId_PlanId_Period_Muscle"
                ON muscle_week_observations ("UserId", "PlanId", "Period", "Muscle");
            CREATE INDEX "IX_muscle_week_observations_PlanId_MesocycleSequence"
                ON muscle_week_observations ("PlanId", "MesocycleSequence");

            CREATE TABLE longitudinal_decisions (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "MesocycleSequence" integer NOT NULL,
                "Muscle" character varying(30) NOT NULL,
                "Action" character varying(10) NOT NULL,
                "BaselineSets" numeric(5,1) NOT NULL,
                "PreviousSets" numeric(5,1) NOT NULL,
                "ProposedSets" numeric(5,1) NOT NULL,
                "AnalyzedFromPeriod" integer NOT NULL,
                "AnalyzedToPeriod" integer NOT NULL,
                "SignalsJson" character varying(2000) NOT NULL,
                "Reason" character varying(600) NOT NULL,
                "PolicyVersion" character varying(30) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_longitudinal_decisions_PlanId_MesocycleSequence_Muscle"
                ON longitudinal_decisions ("PlanId", "MesocycleSequence", "Muscle");
            CREATE INDEX "IX_longitudinal_decisions_UserId_CreatedAt"
                ON longitudinal_decisions ("UserId", "CreatedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS longitudinal_decisions;
            DROP TABLE IF EXISTS muscle_week_observations;
        """);
    }
}
