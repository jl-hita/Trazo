using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
using Trazo.Domain.Marketing;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class MarketingCampaignConfiguration : IEntityTypeConfiguration<MarketingCampaign>
{
    public void Configure(EntityTypeBuilder<MarketingCampaign> b)
    {
        b.ToTable("marketing_campaigns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(60);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class CampaignAttributionConfiguration : IEntityTypeConfiguration<CampaignAttribution>
{
    public void Configure(EntityTypeBuilder<CampaignAttribution> b)
    {
        b.ToTable("campaign_attributions");
        b.HasKey(x => x.UserId);
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.CampaignId);
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<MarketingCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CampaignDailyStatConfiguration : IEntityTypeConfiguration<CampaignDailyStat>
{
    public void Configure(EntityTypeBuilder<CampaignDailyStat> b)
    {
        b.ToTable("campaign_daily_stats");
        b.HasKey(x => new { x.CampaignId, x.Day });
        b.HasOne<MarketingCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CampaignSpendConfiguration : IEntityTypeConfiguration<CampaignSpend>
{
    public void Configure(EntityTypeBuilder<CampaignSpend> b)
    {
        b.ToTable("campaign_spend");
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(60);
        b.HasIndex(x => new { x.CampaignId, x.Day });
        b.HasOne<MarketingCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StoreDownloadsConfiguration : IEntityTypeConfiguration<StoreDownloads>
{
    public void Configure(EntityTypeBuilder<StoreDownloads> b)
    {
        b.ToTable("store_downloads");
        b.HasKey(x => x.Week);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.UpdatedBy).HasMaxLength(60);
    }
}
