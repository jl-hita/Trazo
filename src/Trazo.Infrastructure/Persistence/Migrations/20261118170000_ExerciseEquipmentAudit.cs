using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261118170000_ExerciseEquipmentAudit")]
public sealed class ExerciseEquipmentAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE exercise_catalog
            SET "RequiredEquipment" = CASE exercise_id
                WHEN 'bent-knee-inverted-row' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 'inverted-row' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 't-bar-row' THEN ARRAY['barbell', 'landmine', 't-bar-row-handle']::text[]
                WHEN 'chest-supported-t-bar-row' THEN ARRAY['barbell', 'landmine', 't-bar-row-handle', 'bench']::text[]
                WHEN 'preacher-curl' THEN ARRAY['ez-bar', 'preacher-curl-bench']::text[]
                WHEN 'front-squat' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 'squat' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 'bench-press' THEN ARRAY['barbell', 'bench', 'squat-rack']::text[]
                WHEN 'close-grip-bench-press' THEN ARRAY['barbell', 'bench', 'squat-rack']::text[]
                WHEN 'incline-bench-press' THEN ARRAY['barbell', 'bench', 'squat-rack']::text[]
                WHEN 'paused-bench-press' THEN ARRAY['barbell', 'bench', 'squat-rack']::text[]
                WHEN 'ohp' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 'push-press' THEN ARRAY['barbell', 'squat-rack']::text[]
                WHEN 'lat-pulldown' THEN ARRAY['lat-pulldown-machine']::text[]
                WHEN 'step-ups' THEN ARRAY['step']::text[]
                ELSE "RequiredEquipment"
            END,
            "UpdatedAt" = NOW()
            WHERE exercise_id IN (
                'bent-knee-inverted-row', 'inverted-row', 't-bar-row', 'chest-supported-t-bar-row',
                'preacher-curl', 'front-squat', 'squat', 'bench-press', 'close-grip-bench-press',
                'incline-bench-press', 'paused-bench-press', 'ohp', 'push-press', 'lat-pulldown', 'step-ups'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not restore the audited incomplete requirements. The previous arrays omitted
        // physical supports or used a generic implement rather than the actual setup.
    }
}
