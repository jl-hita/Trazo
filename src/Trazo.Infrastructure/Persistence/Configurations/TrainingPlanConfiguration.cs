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
        builder.Property(x => x.Goal).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Experience).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Split).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TrainingDays).AsJsonText();
        builder.Property(x => x.AvailableEquipment).AsJsonText();
        builder.Property(x => x.ExcludedExerciseIds).AsJsonText();
        builder.Property(x => x.Limitations).AsJsonText();
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
    public void Configure(EntityTypeBuilder<TrainingMesocycle> builder)
    {
        builder.ToTable("training_mesocycles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PhaseType).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.Sequence }).IsUnique();
        builder.HasMany(x => x.Sessions).WithOne().HasForeignKey(x => x.MesocycleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlannedSessionConfiguration : IEntityTypeConfiguration<PlannedSession>
{
    public void Configure(EntityTypeBuilder<PlannedSession> builder)
    {
        builder.ToTable("planned_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PrescriptionJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.Date });
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
        builder.HasIndex(x => x.PlannedSessionId).IsUnique();
        builder.HasIndex(x => new { x.PlanId, x.LoggedAt });
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
        builder.ToTable("plan_adaptations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ExerciseIds).AsJsonText();
        builder.Property(x => x.Explanation).HasMaxLength(600).IsRequired();
        builder.HasIndex(x => new { x.PlanId, x.AppliedAt });
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}
