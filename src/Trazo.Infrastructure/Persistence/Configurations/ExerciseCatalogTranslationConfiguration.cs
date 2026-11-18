using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Training;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class ExerciseCatalogTranslationConfiguration : IEntityTypeConfiguration<ExerciseCatalogTranslation>
{
    public void Configure(EntityTypeBuilder<ExerciseCatalogTranslation> builder)
    {
        builder.ToTable("exercise_catalog_translations", table =>
        {
            table.HasCheckConstraint(
                "CK_exercise_catalog_translations_language",
                "\"LanguageCode\" ~ '^[a-z]{2,3}(-[A-Z]{2})?$'");
            table.HasCheckConstraint(
                "CK_exercise_catalog_translations_status",
                "\"Status\" IN ('draft', 'review', 'published')");
        });

        builder.HasKey(x => new { x.ExerciseId, x.LanguageCode });
        builder.Property(x => x.ExerciseId).HasColumnName("exercise_id").HasMaxLength(100);
        builder.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description);
        builder.Property(x => x.Instructions).HasColumnType("text[]");
        builder.Property(x => x.Tips).HasColumnType("text[]");
        builder.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("draft").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasOne<ExerciseCatalogItem>().WithMany()
            .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LanguageCode, x.Status });
    }
}
