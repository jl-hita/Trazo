using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
using Trazo.Domain.Push;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Token).HasMaxLength(1024).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
