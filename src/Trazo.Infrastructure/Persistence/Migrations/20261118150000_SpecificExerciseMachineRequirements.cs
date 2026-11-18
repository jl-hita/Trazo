using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261118150000_SpecificExerciseMachineRequirements")]
public sealed class SpecificExerciseMachineRequirements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE exercise_catalog
            SET "RequiredEquipment" = CASE exercise_id
                WHEN 'assisted-pull-ups' THEN ARRAY['assisted-pull-up-machine']::text[]
                WHEN 'chest-press-machine' THEN ARRAY['chest-press-machine']::text[]
                WHEN 'chest-supported-machine-row' THEN ARRAY['seated-row-machine']::text[]
                WHEN 'hip-abduction' THEN ARRAY['hip-abduction-machine']::text[]
                WHEN 'hip-adduction' THEN ARRAY['hip-adduction-machine']::text[]
                WHEN 'machine-back-extension' THEN ARRAY['back-extension-machine']::text[]
                WHEN 'machine-bicep-curl' THEN ARRAY['bicep-curl-machine']::text[]
                WHEN 'machine-calf-raise' THEN ARRAY['standing-calf-raise-machine']::text[]
                WHEN 'machine-chest-fly' THEN ARRAY['chest-fly-machine']::text[]
                WHEN 'machine-reverse-fly' THEN ARRAY['reverse-fly-machine']::text[]
                WHEN 'machine-seated-crunch' THEN ARRAY['ab-crunch-machine']::text[]
                WHEN 'machine-shoulder-press' THEN ARRAY['shoulder-press-machine']::text[]
                WHEN 'pec-deck' THEN ARRAY['pec-deck']::text[]
                WHEN 'plate-loaded-glute-drive' THEN ARRAY['hip-thrust-machine']::text[]
                WHEN 'seated-calf-raise' THEN ARRAY['seated-calf-raise-machine']::text[]
                WHEN 'single-arm-plate-loaded-lateral-raise' THEN ARRAY['plate-loaded-lateral-raise-machine']::text[]
                WHEN 'standing-calf-raise' THEN ARRAY['standing-calf-raise-machine']::text[]
                WHEN 'v-bar-lat-pulldown' THEN ARRAY['lat-pulldown-machine']::text[]
            END,
            "UpdatedAt" = NOW()
            WHERE exercise_id IN (
                'assisted-pull-ups', 'chest-press-machine', 'chest-supported-machine-row',
                'hip-abduction', 'hip-adduction', 'machine-back-extension', 'machine-bicep-curl',
                'machine-calf-raise', 'machine-chest-fly', 'machine-reverse-fly', 'machine-seated-crunch',
                'machine-shoulder-press', 'pec-deck', 'plate-loaded-glute-drive', 'seated-calf-raise',
                'single-arm-plate-loaded-lateral-raise', 'standing-calf-raise', 'v-bar-lat-pulldown'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The previous values were inconsistent across imported source records. Reverting to
        // a generic "machine" requirement would reintroduce a known data-quality defect.
    }
}
