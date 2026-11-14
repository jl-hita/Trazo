using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Training plans: the profile and macrocycle phases of a user's plan, the blocks (mesocycles) and sessions generated from
/// it, the workouts the user logged and the adaptations the engine applied. Lists are stored as JSON text.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114120000_TrainingPlans")]
public sealed class TrainingPlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE training_plans (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "Status" character varying(20) NOT NULL,
                "Goal" character varying(40) NOT NULL,
                "Experience" character varying(40) NOT NULL,
                "Split" character varying(40) NOT NULL,
                "StartDate" date NOT NULL,
                "TotalWeeks" integer NOT NULL,
                "TrainingDays" text NOT NULL,
                "MaxSessionMinutes" integer NOT NULL,
                "AvailableEquipment" text NOT NULL,
                "ExcludedExerciseIds" text NOT NULL,
                "Limitations" text NOT NULL,
                "IncludeDeloadPhases" boolean NOT NULL,
                "DeloadEveryTrainingWeeks" integer NOT NULL,
                "Seed" integer NOT NULL,
                "EngineVersion" character varying(40) NOT NULL,
                "MacrocycleName" character varying(160) NOT NULL,
                "MacrocycleNotes" character varying(1000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX ux_training_plans_one_active_per_user ON training_plans ("UserId") WHERE "Status" = 'Active';

            CREATE TABLE training_plan_phases (
                "Id" uuid PRIMARY KEY,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "SequenceNumber" integer NOT NULL,
                "Name" character varying(120) NOT NULL,
                "PhaseType" character varying(30) NOT NULL,
                "StartDate" date NOT NULL,
                "EndDate" date NOT NULL,
                "Objective" character varying(400) NOT NULL
            );
            CREATE UNIQUE INDEX "IX_training_plan_phases_PlanId_SequenceNumber" ON training_plan_phases ("PlanId", "SequenceNumber");

            CREATE TABLE training_mesocycles (
                "Id" uuid PRIMARY KEY,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "Sequence" integer NOT NULL,
                "PhaseSequenceNumber" integer NOT NULL,
                "PhaseType" character varying(30) NOT NULL,
                "IsDeload" boolean NOT NULL,
                "StartWeek" integer NOT NULL,
                "Weeks" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX "IX_training_mesocycles_PlanId_Sequence" ON training_mesocycles ("PlanId", "Sequence");

            CREATE TABLE planned_sessions (
                "Id" uuid PRIMARY KEY,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id"),
                "MesocycleId" uuid NOT NULL REFERENCES training_mesocycles ("Id") ON DELETE CASCADE,
                "WeekNumber" integer NOT NULL,
                "Date" date NOT NULL,
                "Title" character varying(160) NOT NULL,
                "EstimatedDurationMinutes" integer NOT NULL,
                "PrescriptionJson" text NOT NULL,
                "Status" character varying(20) NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_planned_sessions_PlanId_Date" ON planned_sessions ("PlanId", "Date");
            CREATE INDEX "IX_planned_sessions_MesocycleId" ON planned_sessions ("MesocycleId");

            CREATE TABLE workout_logs (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "PlannedSessionId" uuid NOT NULL REFERENCES planned_sessions ("Id") ON DELETE CASCADE,
                "LoggedAt" timestamp with time zone NOT NULL,
                "Outcome" character varying(20) NOT NULL,
                "NonCompletionReason" character varying(20) NOT NULL,
                "Recovery" character varying(20) NOT NULL,
                "SessionRpe" numeric(3,1) NULL,
                "PainReported" boolean NOT NULL,
                "Notes" character varying(1000) NULL
            );
            CREATE UNIQUE INDEX "IX_workout_logs_PlannedSessionId" ON workout_logs ("PlannedSessionId");
            CREATE INDEX "IX_workout_logs_PlanId_LoggedAt" ON workout_logs ("PlanId", "LoggedAt");
            CREATE INDEX "IX_workout_logs_UserId" ON workout_logs ("UserId");

            CREATE TABLE workout_set_logs (
                "Id" uuid PRIMARY KEY,
                "WorkoutLogId" uuid NOT NULL REFERENCES workout_logs ("Id") ON DELETE CASCADE,
                "ExerciseId" character varying(100) NOT NULL,
                "SetNumber" integer NOT NULL,
                "WeightKg" numeric(7,2) NULL,
                "Repetitions" integer NULL,
                "Rir" numeric(3,1) NULL,
                "Completed" boolean NOT NULL,
                "SkippedReason" character varying(20) NULL,
                "PainReported" boolean NOT NULL
            );
            CREATE UNIQUE INDEX "IX_workout_set_logs_WorkoutLogId_ExerciseId_SetNumber" ON workout_set_logs ("WorkoutLogId", "ExerciseId", "SetNumber");
            CREATE INDEX "IX_workout_set_logs_ExerciseId" ON workout_set_logs ("ExerciseId");

            CREATE TABLE plan_adaptations (
                "Id" uuid PRIMARY KEY,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "WorkoutLogId" uuid NULL,
                "Kind" character varying(30) NOT NULL,
                "ExerciseIds" text NOT NULL,
                "SessionsChanged" integer NOT NULL,
                "Explanation" character varying(600) NOT NULL,
                "AppliedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_plan_adaptations_PlanId_AppliedAt" ON plan_adaptations ("PlanId", "AppliedAt");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS plan_adaptations;
            DROP TABLE IF EXISTS workout_set_logs;
            DROP TABLE IF EXISTS workout_logs;
            DROP TABLE IF EXISTS planned_sessions;
            DROP TABLE IF EXISTS training_mesocycles;
            DROP TABLE IF EXISTS training_plan_phases;
            DROP TABLE IF EXISTS training_plans;
        """);
    }
}
