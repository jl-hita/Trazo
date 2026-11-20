using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Persistence.Configurations;

/// <summary>Stores string lists as JSON text so the same mapping works on PostgreSQL and SQLite (tests).</summary>
internal static class StringListMapping
{
    public static PropertyBuilder<string[]> AsJsonText(this PropertyBuilder<string[]> property)
    {
        property.HasConversion(
            list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<string>(),
            new ValueComparer<string[]>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                list => list.ToArray()));
        property.HasColumnType("text").IsRequired();
        return property;
    }
}

public sealed class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        builder.ToTable("training_plans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.SchedulingMode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Goal).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Experience).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Split).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TrainingDays).AsJsonText();
        builder.Property(x => x.AvailableEquipment).AsJsonText();
        builder.Property(x => x.ExcludedExerciseIds).AsJsonText();
        builder.Property(x => x.Limitations).AsJsonText();
        builder.Property(x => x.MusclePriorities).AsJsonText();
        builder.Property(x => x.EngineVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.MacrocycleName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.MacrocycleNotes).HasMaxLength(1000).IsRequired();
        // One active plan per user, enforced by the database so two concurrent requests cannot create two.
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName("ux_training_plans_one_active_per_user");
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Phases).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Mesocycles).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TrainingPlanPhaseConfiguration : IEntityTypeConfiguration<TrainingPlanPhase>
{
    public void Configure(EntityTypeBuilder<TrainingPlanPhase> builder)
    {
        builder.ToTable("training_plan_phases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PhaseType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Objective).HasMaxLength(400).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.SequenceNumber }).IsUnique();
    }
}

public sealed class TrainingMesocycleConfiguration : IEntityTypeConfiguration<TrainingMesocycle>
{
    private const string TraceCheck =
        "(\"GenerationSnapshotJson\" IS NULL AND \"SnapshotSchemaVersion\" IS NULL AND \"EngineVersion\" IS NULL AND \"CatalogFingerprint\" IS NULL AND \"GeneratedAt\" IS NULL)"
        + " OR (\"GenerationSnapshotJson\" IS NOT NULL AND \"SnapshotSchemaVersion\" IS NOT NULL AND \"EngineVersion\" IS NOT NULL AND \"CatalogFingerprint\" IS NOT NULL AND \"GeneratedAt\" IS NOT NULL)";

    public void Configure(EntityTypeBuilder<TrainingMesocycle> builder)
    {
        // The trace columns are all present or all absent: a block has a complete trace or none (generated before it existed, or manual).
        builder.ToTable("training_mesocycles", table => table.HasCheckConstraint("CK_training_mesocycles_trace_complete", TraceCheck));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PhaseType).HasMaxLength(30).IsRequired();
        // Nullable on purpose: no value is invented for blocks generated before the trace existed.
        builder.Property(x => x.GenerationSnapshotJson).HasColumnType("text");
        builder.Property(x => x.EngineVersion).HasMaxLength(40);
        builder.Property(x => x.CatalogFingerprint).HasMaxLength(64);
        // Immutable: once written, EF throws if code tries to change any of them.
        foreach (var trace in new[] { nameof(TrainingMesocycle.GenerationSnapshotJson), nameof(TrainingMesocycle.SnapshotSchemaVersion),
                     nameof(TrainingMesocycle.EngineVersion), nameof(TrainingMesocycle.CatalogFingerprint), nameof(TrainingMesocycle.GeneratedAt) })
            builder.Property(trace).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.HasIndex(x => new { x.PlanId, x.Sequence }).IsUnique();
        builder.HasMany(x => x.Sessions).WithOne().HasForeignKey(x => x.MesocycleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlannedSessionConfiguration : IEntityTypeConfiguration<PlannedSession>
{
    public void Configure(EntityTypeBuilder<PlannedSession> builder)
    {
        // The original prescription is all-or-nothing (snapshot and its layout version) and only an engine session has one.
        builder.ToTable("planned_sessions", table =>
        {
            table.HasCheckConstraint("CK_planned_sessions_original_complete",
                "(\"OriginalPrescriptionJson\" IS NULL AND \"OriginalPrescriptionSchemaVersion\" IS NULL)"
                + " OR (\"OriginalPrescriptionJson\" IS NOT NULL AND \"OriginalPrescriptionSchemaVersion\" IS NOT NULL AND \"PrescriptionSource\" = 'engine')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PrescriptionJson).HasColumnType("text").IsRequired();
        // Nullable on purpose: nothing is invented for sessions created before the snapshot existed. Immutable once written.
        builder.Property(x => x.OriginalPrescriptionJson).HasColumnType("text");
        builder.Property(x => x.PrescriptionSource).HasMaxLength(10);
        foreach (var original in new[] { nameof(PlannedSession.OriginalPrescriptionJson), nameof(PlannedSession.OriginalPrescriptionSchemaVersion), nameof(PlannedSession.PrescriptionSource) })
            builder.Property(original).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        // Portable optimistic concurrency (no provider-specific xmin/rowversion): an explicit counter that the context advances on every UPDATE.
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.PlanId, x.Date });
        builder.HasIndex(x => new { x.PlanId, x.SequenceIndex }).IsUnique();
        // NoAction: the block's cascade removes the sessions; a second cascade path from the plan is not needed.
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WorkoutLogConfiguration : IEntityTypeConfiguration<WorkoutLog>
{
    public void Configure(EntityTypeBuilder<WorkoutLog> builder)
    {
        builder.ToTable("workout_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.NonCompletionReason).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Recovery).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SessionRpe).HasPrecision(3, 1);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.ExerciseFeedbackJson).HasMaxLength(4000);
        builder.HasIndex(x => x.PlannedSessionId).IsUnique();
        builder.HasIndex(x => new { x.PlanId, x.LoggedAt });
        builder.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.PerformedAt });
        builder.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlannedSession>().WithMany().HasForeignKey(x => x.PlannedSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Sets).WithOne().HasForeignKey(x => x.WorkoutLogId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WorkoutSetLogConfiguration : IEntityTypeConfiguration<WorkoutSetLog>
{
    public void Configure(EntityTypeBuilder<WorkoutSetLog> builder)
    {
        builder.ToTable("workout_set_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExerciseId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.WeightKg).HasPrecision(7, 2);
        builder.Property(x => x.Rir).HasPrecision(3, 1);
        builder.Property(x => x.SkippedReason).HasMaxLength(20);
        builder.HasIndex(x => new { x.WorkoutLogId, x.ExerciseId, x.SetNumber }).IsUnique();
        builder.HasIndex(x => x.ExerciseId);
    }
}

public sealed class PlanAdaptationConfiguration : IEntityTypeConfiguration<PlanAdaptation>
{
    public void Configure(EntityTypeBuilder<PlanAdaptation> builder)
    {
        builder.ToTable("plan_adaptations", table => table.HasCheckConstraint(
            "CK_plan_adaptations_changes_versioned", "(\"ChangesJson\" IS NULL) = (\"SchemaVersion\" IS NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ExerciseIds).AsJsonText();
        builder.Property(x => x.Explanation).HasMaxLength(600).IsRequired();
        // Nullable on purpose: rows written before the trace existed carry no source, version or changes, and none is invented for them.
        builder.Property(x => x.Source).HasMaxLength(10);
        builder.Property(x => x.EngineVersion).HasMaxLength(40);
        builder.Property(x => x.ChangesJson).HasColumnType("text");
        // Who and with which engine never change once the row exists.
        foreach (var fixedColumn in new[] { nameof(PlanAdaptation.Source), nameof(PlanAdaptation.EngineVersion) })
            builder.Property(fixedColumn).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.HasIndex(x => new { x.PlanId, x.AppliedAt });
        // One lighter first session per absence, even if two requests arrive at once: the second insert fails and changes nothing.
        builder.HasIndex(x => new { x.PlanId, x.EvidenceAt }).IsUnique().HasFilter("\"Kind\" = 'reentry'");
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}
