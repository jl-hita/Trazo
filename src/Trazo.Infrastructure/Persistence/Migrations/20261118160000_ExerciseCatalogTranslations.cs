using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261118160000_ExerciseCatalogTranslations")]
public sealed class ExerciseCatalogTranslations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE exercise_catalog_translations (
                exercise_id character varying(100) NOT NULL,
                "LanguageCode" character varying(10) NOT NULL,
                "Name" character varying(200) NULL,
                "Description" text NULL,
                "Instructions" text[] NULL,
                "Tips" text[] NULL,
                "Status" character varying(16) NOT NULL DEFAULT 'draft',
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_exercise_catalog_translations" PRIMARY KEY (exercise_id, "LanguageCode"),
                CONSTRAINT "FK_exercise_catalog_translations_exercise_catalog_exercise_id"
                    FOREIGN KEY (exercise_id) REFERENCES exercise_catalog (exercise_id) ON DELETE CASCADE,
                CONSTRAINT "CK_exercise_catalog_translations_language"
                    CHECK ("LanguageCode" ~ '^[a-z]{2,3}(-[A-Z]{2})?$'),
                CONSTRAINT "CK_exercise_catalog_translations_status"
                    CHECK ("Status" IN ('draft', 'review', 'published'))
            );
            CREATE INDEX "IX_exercise_catalog_translations_LanguageCode_Status"
                ON exercise_catalog_translations ("LanguageCode", "Status");

            -- Preserve the existing Spanish content as the complete source translation.
            INSERT INTO exercise_catalog_translations
                (exercise_id, "LanguageCode", "Name", "Description", "Instructions", "Tips", "Status", "UpdatedAt")
            SELECT exercise_id, 'es', "NameEs", "DescriptionEs", "InstructionsEs", "TipsEs", 'published', "UpdatedAt"
            FROM exercise_catalog;

            -- English names already exist, but descriptions, instructions and tips do not.
            -- Keep these rows explicitly as drafts; clients must fall back field-by-field to Spanish.
            INSERT INTO exercise_catalog_translations
                (exercise_id, "LanguageCode", "Name", "Status", "UpdatedAt")
            SELECT exercise_id, 'en', "NameEn", 'draft', "UpdatedAt"
            FROM exercise_catalog;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "exercise_catalog_translations");
    }
}
