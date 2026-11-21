using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Support;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class ErrorReportOutboxConfiguration : IEntityTypeConfiguration<ErrorReportOutboxItem>
{
    public void Configure(EntityTypeBuilder<ErrorReportOutboxItem> b)
    {
        b.ToTable("error_report_outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Source).HasMaxLength(16).IsRequired();
        b.Property(x => x.AppVersion).HasMaxLength(40);
        // Twice the ticket limit: the retrace of an app trace may be longer than what is finally stored.
        b.Property(x => x.StackTrace).HasMaxLength(40000).IsRequired();
        b.Property(x => x.TraceId).HasMaxLength(64);
        b.HasIndex(x => x.NextAttemptAt);
    }
}
