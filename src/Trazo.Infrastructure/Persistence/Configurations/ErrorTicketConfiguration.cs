using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Support;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class ErrorTicketConfiguration : IEntityTypeConfiguration<ErrorTicket>
{
    public void Configure(EntityTypeBuilder<ErrorTicket> b)
    {
        b.ToTable("error_tickets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.Source).HasMaxLength(16).IsRequired();
        b.Property(x => x.AppVersion).HasMaxLength(40);
        b.Property(x => x.ErrorType).HasMaxLength(200).IsRequired();
        b.Property(x => x.StackTrace).HasMaxLength(16000).IsRequired();
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.HasIndex(x => x.Fingerprint).IsUnique();
        b.HasIndex(x => x.LastSeenAt);
    }
}
