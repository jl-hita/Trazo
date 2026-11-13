using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.UnitTests;

/// <summary>SQLite stand-ins for the PostgreSQL exercise tables (arrays are stored as JSON text).</summary>
internal static class ExerciseTestSchema
{
    public static void Create(TrazoDbContext db) => db.Database.ExecuteSqlRaw("""
        CREATE TABLE exercise_catalog (
          exercise_id TEXT PRIMARY KEY, "Source" TEXT NOT NULL, "NameEn" TEXT NOT NULL, "NameEs" TEXT NOT NULL,
          "DescriptionEs" TEXT NOT NULL, "InstructionsEs" TEXT NOT NULL, "TipsEs" TEXT NOT NULL, "ForceType" TEXT NOT NULL,
          "Mechanic" TEXT NOT NULL, "Difficulty" TEXT NOT NULL, "Equipment" TEXT NULL, "BodyPart" TEXT NOT NULL,
          "Met" REAL NOT NULL, "IsUnilateral" INTEGER NOT NULL, "IsBodyweight" INTEGER NOT NULL, "MovementPattern" TEXT NOT NULL,
          "PrimaryMuscles" TEXT NOT NULL, "SecondaryMuscles" TEXT NOT NULL, "MinimumExperience" TEXT NOT NULL,
          "SuitableGoals" TEXT NOT NULL, "RequiredEquipment" TEXT NOT NULL, "SelectionPriority" INTEGER NOT NULL,
          "SeedVersion" INTEGER NOT NULL, "IsActive" INTEGER NOT NULL DEFAULT 1, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
        CREATE TABLE exercise_catalog_import_metadata (
          exercise_id TEXT PRIMARY KEY, source_system TEXT NOT NULL, is_locally_curated INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE exercise_catalog_images (exercise_id TEXT NOT NULL, slot INTEGER NOT NULL, PRIMARY KEY (exercise_id, slot));
        """);
}
