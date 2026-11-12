using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trazo.Domain.Training;

namespace Trazo.Infrastructure.Persistence.Configurations;

public sealed class ExerciseCatalogItemConfiguration : IEntityTypeConfiguration<ExerciseCatalogItem>
{
    public void Configure(EntityTypeBuilder<ExerciseCatalogItem> builder)
    {
        builder.ToTable("exercise_catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("exercise_id").HasMaxLength(100);
        builder.Property(x => x.Source).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEs).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionEs).IsRequired();
        builder.Property(x => x.InstructionsEs).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.TipsEs).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ForceType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Mechanic).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Difficulty).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Equipment).HasMaxLength(120);
        builder.Property(x => x.BodyPart).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MovementPattern).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PrimaryMuscles).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.SecondaryMuscles).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.MinimumExperience).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SuitableGoals).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.RequiredEquipment).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.Source);
        builder.HasIndex(x => x.MovementPattern);
    }
}
