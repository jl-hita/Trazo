using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Fiscal;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class FiscalEntryConfiguration : IEntityTypeConfiguration<FiscalEntry>
{
    public void Configure(EntityTypeBuilder<FiscalEntry> b)
    {
        b.ToTable("fiscal_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(10).IsRequired();
        b.Property(x => x.Period).HasMaxLength(7).IsRequired();
        b.Property(x => x.Concept).HasMaxLength(200).IsRequired();
        b.Property(x => x.Counterparty).HasMaxLength(150);
        b.Property(x => x.CounterpartyTaxId).HasMaxLength(30);
        b.Property(x => x.CounterpartyCountry).HasMaxLength(2);
        b.Property(x => x.InvoiceNumber).HasMaxLength(60);
        b.Property(x => x.Category).HasMaxLength(30).IsRequired();
        b.Property(x => x.VatTreatment).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(60);
        foreach (var money in new[] { nameof(FiscalEntry.Base), nameof(FiscalEntry.VatAmount), nameof(FiscalEntry.WithholdingAmount), nameof(FiscalEntry.Total) })
            b.Property(money).HasPrecision(14, 2);
        foreach (var pct in new[] { nameof(FiscalEntry.VatRatePct), nameof(FiscalEntry.WithholdingPct), nameof(FiscalEntry.DeductiblePct) })
            b.Property(pct).HasPrecision(5, 2);
        b.HasIndex(x => new { x.Period, x.Kind, x.Date });
    }
}

public sealed class FiscalClosureConfiguration : IEntityTypeConfiguration<FiscalClosure>
{
    public void Configure(EntityTypeBuilder<FiscalClosure> b)
    {
        b.ToTable("fiscal_closures");
        b.HasKey(x => x.Period);
        b.Property(x => x.Period).HasMaxLength(7);
        b.Property(x => x.ClosedBy).HasMaxLength(60).IsRequired();
        b.Property(x => x.SnapshotJson).HasMaxLength(4000).IsRequired();
    }
}

public sealed class FiscalAuditEntryConfiguration : IEntityTypeConfiguration<FiscalAuditEntry>
{
    public void Configure(EntityTypeBuilder<FiscalAuditEntry> b)
    {
        b.ToTable("fiscal_audit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Period).HasMaxLength(7).IsRequired();
        b.Property(x => x.Action).HasMaxLength(12).IsRequired();
        b.Property(x => x.Actor).HasMaxLength(60).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => x.At);
    }
}
