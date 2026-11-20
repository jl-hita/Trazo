using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Traceability of the prescription: the original prescription of each engine-generated session, and for each adaptation who made it,
/// with which engine and which sessions changed from what to what. Every column is nullable and has NO default: sessions and
/// adaptations that already exist stay "without trace" (nothing is rebuilt from today's catalog or engine rules, and no historical row
/// is rewritten). Purely additive: no existing column, index or constraint changes.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261128100000_PrescriptionAdaptationHistory")]
public sealed class PrescriptionAdaptationHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE planned_sessions
                ADD COLUMN "OriginalPrescriptionJson" text NULL,
                ADD COLUMN "OriginalPrescriptionSchemaVersion" integer NULL,
                ADD COLUMN "PrescriptionSource" character varying(10) NULL;
            ALTER TABLE planned_sessions
                ADD CONSTRAINT "CK_planned_sessions_original_complete" CHECK (
                    ("OriginalPrescriptionJson" IS NULL AND "OriginalPrescriptionSchemaVersion" IS NULL)
                    OR ("OriginalPrescriptionJson" IS NOT NULL AND "OriginalPrescriptionSchemaVersion" IS NOT NULL AND "PrescriptionSource" = 'engine'));
            ALTER TABLE plan_adaptations
                ADD COLUMN "Source" character varying(10) NULL,
                ADD COLUMN "EngineVersion" character varying(40) NULL,
                ADD COLUMN "SchemaVersion" integer NULL,
                ADD COLUMN "ChangesJson" text NULL;
            ALTER TABLE plan_adaptations
                ADD CONSTRAINT "CK_plan_adaptations_changes_versioned" CHECK (("ChangesJson" IS NULL) = ("SchemaVersion" IS NULL));
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE plan_adaptations DROP CONSTRAINT IF EXISTS "CK_plan_adaptations_changes_versioned";
            ALTER TABLE plan_adaptations
                DROP COLUMN IF EXISTS "Source", DROP COLUMN IF EXISTS "EngineVersion",
                DROP COLUMN IF EXISTS "SchemaVersion", DROP COLUMN IF EXISTS "ChangesJson";
            ALTER TABLE planned_sessions DROP CONSTRAINT IF EXISTS "CK_planned_sessions_original_complete";
            ALTER TABLE planned_sessions
                DROP COLUMN IF EXISTS "OriginalPrescriptionJson", DROP COLUMN IF EXISTS "OriginalPrescriptionSchemaVersion",
                DROP COLUMN IF EXISTS "PrescriptionSource";
        """);
    }
}
