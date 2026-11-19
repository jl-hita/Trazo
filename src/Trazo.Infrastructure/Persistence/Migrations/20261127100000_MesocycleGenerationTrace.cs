using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Traceability of each generated block: the context it was generated from, the engine version, the catalog fingerprint and the time.
/// All columns are nullable and have NO default: blocks that already exist stay "without trace" (nothing is invented about the engine or
/// the catalog they were generated with). The check constraint keeps the trace all-or-nothing.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261127100000_MesocycleGenerationTrace")]
public sealed class MesocycleGenerationTrace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_mesocycles
                ADD COLUMN "GenerationSnapshotJson" text NULL,
                ADD COLUMN "SnapshotSchemaVersion" integer NULL,
                ADD COLUMN "EngineVersion" character varying(40) NULL,
                ADD COLUMN "CatalogFingerprint" character varying(64) NULL,
                ADD COLUMN "GeneratedAt" timestamp with time zone NULL;
            ALTER TABLE training_mesocycles
                ADD CONSTRAINT "CK_training_mesocycles_trace_complete" CHECK (
                    ("GenerationSnapshotJson" IS NULL AND "SnapshotSchemaVersion" IS NULL AND "EngineVersion" IS NULL AND "CatalogFingerprint" IS NULL AND "GeneratedAt" IS NULL)
                    OR ("GenerationSnapshotJson" IS NOT NULL AND "SnapshotSchemaVersion" IS NOT NULL AND "EngineVersion" IS NOT NULL AND "CatalogFingerprint" IS NOT NULL AND "GeneratedAt" IS NOT NULL));
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE training_mesocycles DROP CONSTRAINT IF EXISTS "CK_training_mesocycles_trace_complete";
            ALTER TABLE training_mesocycles
                DROP COLUMN IF EXISTS "GenerationSnapshotJson", DROP COLUMN IF EXISTS "SnapshotSchemaVersion",
                DROP COLUMN IF EXISTS "EngineVersion", DROP COLUMN IF EXISTS "CatalogFingerprint", DROP COLUMN IF EXISTS "GeneratedAt";
        """);
    }
}
