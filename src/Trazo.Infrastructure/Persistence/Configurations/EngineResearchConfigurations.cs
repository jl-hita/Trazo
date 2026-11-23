using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Persistence.Configurations;

// Deliberately NO foreign key, user id or plan id: nothing here may lead back to a person (see EngineResearchObservation).
public sealed class EngineResearchObservationConfiguration : IEntityTypeConfiguration<EngineResearchObservation>
{
    public void Configure(EntityTypeBuilder<EngineResearchObservation> b)
    {
        b.ToTable("engine_research_observations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Muscle).HasMaxLength(30).IsRequired();
        b.Property(x => x.Quality).HasMaxLength(12).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(30);
        b.Property(x => x.PrescribedSets).HasPrecision(6,1);
        b.Property(x => x.PerformedSets).HasPrecision(6,1);
        b.Property(x => x.PerformanceChangePercent).HasPrecision(8,2);
        b.Property(x => x.PlanGoal).HasMaxLength(30);
        b.Property(x => x.PlanExperience).HasMaxLength(30);
        b.Property(x => x.PlanSplit).HasMaxLength(30);
        b.Property(x => x.PlanReadiness).HasMaxLength(20);
        b.Property(x => x.PlanEngineVersion).HasMaxLength(40);
        b.HasIndex(x => x.SampleGroupId);
    }
}

public sealed class EngineResearchDecisionConfiguration : IEntityTypeConfiguration<EngineResearchDecision>
{
    public void Configure(EntityTypeBuilder<EngineResearchDecision> b)
    {
        b.ToTable("engine_research_decisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Muscle).HasMaxLength(30).IsRequired();
        b.Property(x => x.Action).HasMaxLength(10).IsRequired();
        b.Property(x => x.BaselineSets).HasPrecision(5,1);
        b.Property(x => x.PreviousSets).HasPrecision(5,1);
        b.Property(x => x.ProposedSets).HasPrecision(5,1);
        b.Property(x => x.PolicyVersion).HasMaxLength(30).IsRequired();
        b.Property(x => x.PlanGoal).HasMaxLength(30);
        b.Property(x => x.PlanExperience).HasMaxLength(30);
        b.Property(x => x.PlanSplit).HasMaxLength(30);
        b.Property(x => x.PlanReadiness).HasMaxLength(20);
        b.Property(x => x.PlanEngineVersion).HasMaxLength(40);
        b.HasIndex(x => x.SampleGroupId);
    }
}
