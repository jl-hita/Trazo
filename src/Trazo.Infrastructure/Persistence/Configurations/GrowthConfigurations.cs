using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Growth;
using Trazo.Domain.Identity;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class PremiumGrantConfiguration : IEntityTypeConfiguration<PremiumGrant>
{
    public void Configure(EntityTypeBuilder<PremiumGrant> b)
    {
        b.ToTable("premium_grants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.SourceKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.VoidReason).HasMaxLength(300);
        b.Property(x => x.VoidedBy).HasMaxLength(40);
        b.Property(x => x.LastDeferError).HasMaxLength(300);
        b.Property(x => x.GoogleOrderBefore).HasMaxLength(100);
        b.HasIndex(x => new { x.Kind, x.SourceKey }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasIndex(x => x.Status);
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> b)
    {
        b.ToTable("referral_codes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(12).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReferralRelationshipConfiguration : IEntityTypeConfiguration<ReferralRelationship>
{
    public void Configure(EntityTypeBuilder<ReferralRelationship> b)
    {
        b.ToTable("referral_relationships");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(12).IsRequired();
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.AppVersion).HasMaxLength(40);
        b.Property(x => x.ConversionPlan).HasMaxLength(200);
        b.Property(x => x.Incident).HasMaxLength(40);
        b.HasIndex(x => x.ReferredUserId).IsUnique();
        b.HasIndex(x => new { x.ReferrerUserId, x.Status });
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Code);
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.ReferrerUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.ReferredUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GrowthEventConfiguration : IEntityTypeConfiguration<GrowthEvent>
{
    public void Configure(EntityTypeBuilder<GrowthEvent> b)
    {
        b.ToTable("growth_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(30);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Plan).HasMaxLength(200);
        b.Property(x => x.AppVersion).HasMaxLength(40);
        b.Property(x => x.Variant).HasMaxLength(40);
        b.Property(x => x.DedupeKey).HasMaxLength(120);
        b.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
        b.HasIndex(x => new { x.Name, x.OccurredAt });
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => new { x.Reference, x.Name });
        // Deleting an account leaves anonymous rows behind: aggregates stay right and nothing identifies a person.
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
