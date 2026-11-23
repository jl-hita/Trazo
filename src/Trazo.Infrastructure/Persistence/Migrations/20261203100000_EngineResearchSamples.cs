using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Anonymous research records for the engine, written when an account is deleted (see docs/account-deletion.md), and the optional
/// research-consent flag of the user. The two new tables carry no user id, plan id or date. Purely additive, so a previous version that is
/// still running during a rolling deploy is not affected.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261203100000_EngineResearchSamples")]
public sealed class EngineResearchSamples : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE users ADD COLUMN "ResearchConsent" boolean NOT NULL DEFAULT FALSE;""");
        migrationBuilder.Sql("""
            CREATE TABLE engine_research_observations (
                "Id" uuid PRIMARY KEY,
                "SampleGroupId" uuid NULL,
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
                "PlanGoal" character varying(30) NULL,
                "PlanExperience" character varying(30) NULL,
                "PlanSplit" character varying(30) NULL,
                "PlanReadiness" character varying(20) NULL,
                "PlanEngineVersion" character varying(40) NULL,
                "PlanSessionsPerPeriod" integer NOT NULL,
                "PlanTotalPeriodsBand" integer NOT NULL
            );
            CREATE INDEX "IX_engine_research_observations_SampleGroupId" ON engine_research_observations ("SampleGroupId");
        """);
        migrationBuilder.Sql("""
            CREATE TABLE engine_research_decisions (
                "Id" uuid PRIMARY KEY,
                "SampleGroupId" uuid NULL,
                "MesocycleSequence" integer NOT NULL,
                "Muscle" character varying(30) NOT NULL,
                "Action" character varying(10) NOT NULL,
                "BaselineSets" numeric(5,1) NOT NULL,
                "PreviousSets" numeric(5,1) NOT NULL,
                "ProposedSets" numeric(5,1) NOT NULL,
                "AnalyzedFromPeriod" integer NOT NULL,
                "AnalyzedToPeriod" integer NOT NULL,
                "PolicyVersion" character varying(30) NOT NULL,
                "PlanGoal" character varying(30) NULL,
                "PlanExperience" character varying(30) NULL,
                "PlanSplit" character varying(30) NULL,
                "PlanReadiness" character varying(20) NULL,
                "PlanEngineVersion" character varying(40) NULL,
                "PlanSessionsPerPeriod" integer NOT NULL,
                "PlanTotalPeriodsBand" integer NOT NULL
            );
            CREATE INDEX "IX_engine_research_decisions_SampleGroupId" ON engine_research_decisions ("SampleGroupId");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS engine_research_decisions;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS engine_research_observations;");
        migrationBuilder.Sql("""ALTER TABLE users DROP COLUMN IF EXISTS "ResearchConsent";""");
    }
}
