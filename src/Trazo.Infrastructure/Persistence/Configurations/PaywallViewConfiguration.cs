using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Analytics;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class PaywallViewConfiguration : IEntityTypeConfiguration<PaywallView>
{
    public void Configure(EntityTypeBuilder<PaywallView> b)
    {
        b.ToTable("paywall_views");
        b.HasKey(x => new { x.Day, x.Source });
        b.Property(x => x.Source).HasMaxLength(16).IsRequired();
    }
}
