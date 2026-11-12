using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261112180000_ExerciseLibraryTransfer")]
public sealed class ExerciseLibraryTransfer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE exercise_catalog_import_metadata (
                exercise_id varchar(100) PRIMARY KEY REFERENCES exercise_catalog(exercise_id) ON DELETE CASCADE,
                source_system varchar(40) NOT NULL,
                source_record_json jsonb NOT NULL,
                attribution text NOT NULL,
                review_notes text NULL,
                reviewed_by varchar(120) NULL,
                reviewed_at timestamptz NULL,
                is_locally_curated boolean NOT NULL DEFAULT FALSE,
                imported_at timestamptz NOT NULL DEFAULT NOW()
            );

            CREATE TABLE exercise_catalog_images (
                exercise_id varchar(100) NOT NULL REFERENCES exercise_catalog(exercise_id) ON DELETE CASCADE,
                slot smallint NOT NULL CHECK (slot BETWEEN 0 AND 3),
                content_type varchar(40) NOT NULL,
                data bytea NOT NULL,
                sha256 char(64) NOT NULL,
                source varchar(40) NOT NULL,
                origin_system varchar(40) NOT NULL,
                imported_at timestamptz NOT NULL DEFAULT NOW(),
                PRIMARY KEY (exercise_id, slot)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS exercise_catalog_images;
            DROP TABLE IF EXISTS exercise_catalog_import_metadata;
            """);
    }
}
