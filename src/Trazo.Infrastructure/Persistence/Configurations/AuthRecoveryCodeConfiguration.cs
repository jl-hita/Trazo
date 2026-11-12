using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class AuthRecoveryCodeConfiguration : IEntityTypeConfiguration<AuthRecoveryCode>
{
    public void Configure(EntityTypeBuilder<AuthRecoveryCode> builder)
    {
        builder.ToTable("auth_recovery_codes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.CodeHash }).IsUnique();
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
