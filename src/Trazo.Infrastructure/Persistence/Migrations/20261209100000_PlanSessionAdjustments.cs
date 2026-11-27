using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>The user's lasting changes to the sessions of a generated plan. A new table: nothing stored is touched.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261209100000_PlanSessionAdjustments")]
public sealed class PlanSessionAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE plan_session_adjustments (
            "Id" uuid NOT NULL CONSTRAINT "PK_plan_session_adjustments" PRIMARY KEY,
            "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
            "SessionTitle" character varying(160) NOT NULL,
            "AdjustmentJson" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );
        CREATE UNIQUE INDEX "IX_plan_session_adjustments_PlanId_SessionTitle" ON plan_session_adjustments ("PlanId", "SessionTitle");
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE plan_session_adjustments;
    """);
}
