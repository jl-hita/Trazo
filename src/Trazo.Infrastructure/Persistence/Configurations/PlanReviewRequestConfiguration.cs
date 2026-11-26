using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class PlanReviewRequestConfiguration : IEntityTypeConfiguration<PlanReviewRequest>
{
    public void Configure(EntityTypeBuilder<PlanReviewRequest> builder)
    {
        builder.ToTable("plan_review_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.Status, x.UpdatedAt });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => x.AssignedToUserId);
        builder.HasIndex(x => x.PlanId).IsUnique().HasFilter("\"Status\" <> 'Reviewed'");
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PlanReviewMessageConfiguration : IEntityTypeConfiguration<PlanReviewMessage>
{
    public void Configure(EntityTypeBuilder<PlanReviewMessage> builder)
    {
        builder.ToTable("plan_review_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Author).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.RequestId, x.CreatedAt });
        builder.HasIndex(x => new { x.RequestId, x.ClientRequestId }).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
        builder.HasOne<PlanReviewRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrazoUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
