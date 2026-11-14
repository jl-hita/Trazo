using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class TrazoUserConfiguration : IEntityTypeConfiguration<TrazoUser>
{
    public void Configure(EntityTypeBuilder<TrazoUser> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.GoogleSubject).HasMaxLength(255);
        builder.Property(x => x.Role).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ProtectedAuthenticatorKey).HasMaxLength(1024);
        builder.Property(x => x.TimeZoneId).HasMaxLength(64);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.HasIndex(x => x.GoogleSubject).IsUnique();
        builder.HasIndex(x => x.Role);
    }
}
