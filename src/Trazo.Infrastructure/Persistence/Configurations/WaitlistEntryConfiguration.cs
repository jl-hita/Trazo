using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Waitlist;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("waitlist_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.Language).HasMaxLength(2).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.CreatedAt);
    }
}
