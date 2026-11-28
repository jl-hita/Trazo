using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Support;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class FeedbackEntryConfiguration : IEntityTypeConfiguration<FeedbackEntry>
{
    public void Configure(EntityTypeBuilder<FeedbackEntry> b)
    {
        b.ToTable("feedback_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasMaxLength(12).IsRequired();
        b.Property(x => x.Message).HasMaxLength(FeedbackEntry.MaxMessageLength).IsRequired();
        b.Property(x => x.Screen).HasMaxLength(FeedbackEntry.MaxScreenLength);
        b.Property(x => x.AppVersion).HasMaxLength(40);
        b.Property(x => x.Device).HasMaxLength(FeedbackEntry.MaxDeviceLength);
        b.Property(x => x.Language).HasMaxLength(12);
        b.Property(x => x.Status).HasMaxLength(12).IsRequired();
        b.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
