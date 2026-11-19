using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class MuscleWeekObservationConfiguration : IEntityTypeConfiguration<MuscleWeekObservation>
{
    public void Configure(EntityTypeBuilder<MuscleWeekObservation> builder)
    {
        builder.ToTable("muscle_week_observations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Muscle).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Quality).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(30);
        builder.Property(x => x.PrescribedSets).HasPrecision(6, 1);
        builder.Property(x => x.PerformedSets).HasPrecision(6, 1);
        builder.Property(x => x.PerformanceChangePercent).HasPrecision(8, 2);
        // One row per user, plan, period and muscle: rebuilding a period updates it, so a session can never be counted twice.
        builder.HasIndex(x => new { x.UserId, x.PlanId, x.Period, x.Muscle }).IsUnique();
        builder.HasIndex(x => new { x.PlanId, x.MesocycleSequence });
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LongitudinalDecisionConfiguration : IEntityTypeConfiguration<LongitudinalDecision>
{
    public void Configure(EntityTypeBuilder<LongitudinalDecision> builder)
    {
        builder.ToTable("longitudinal_decisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Muscle).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Confidence).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasPrecision(3, 2);
        builder.Property(x => x.BaselineSets).HasPrecision(5, 1);
        builder.Property(x => x.PreviousSets).HasPrecision(5, 1);
        builder.Property(x => x.ProposedSets).HasPrecision(5, 1);
        builder.Property(x => x.SignalsJson).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(600).IsRequired();
        builder.Property(x => x.PolicyVersion).HasMaxLength(30).IsRequired();
        // One decision per block and muscle: generating the same block twice leaves a single record.
        builder.HasIndex(x => new { x.PlanId, x.MesocycleSequence, x.Muscle }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}
