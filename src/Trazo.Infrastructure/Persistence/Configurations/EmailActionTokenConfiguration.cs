using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
namespace Trazo.Infrastructure.Persistence.Configurations;
public sealed class EmailActionTokenConfiguration : IEntityTypeConfiguration<EmailActionToken>
{
 public void Configure(EntityTypeBuilder<EmailActionToken> b)
 {
  b.ToTable("email_action_tokens");
  b.HasKey(x=>x.Id);
  b.Property(x=>x.TokenHash).HasMaxLength(64).IsRequired();
  b.Property(x=>x.Purpose).HasMaxLength(30).IsRequired();
  b.HasIndex(x=>x.TokenHash).IsUnique();
  b.HasIndex(x=>new{x.UserId,x.Purpose,x.ExpiresAt});
  b.HasOne<TrazoUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);
 }
}
