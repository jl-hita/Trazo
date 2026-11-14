using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ProductId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PurchaseTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProtectedPurchaseToken).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StoreState).HasMaxLength(60);
        builder.Property(x => x.LatestOrderId).HasMaxLength(100);
        builder.HasIndex(x => x.PurchaseTokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
