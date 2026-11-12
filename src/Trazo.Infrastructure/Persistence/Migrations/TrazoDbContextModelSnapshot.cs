using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
public sealed class TrazoDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.Entity("Trazo.Domain.Training.ExerciseCatalogItem", b =>
        {
            b.Property<string>("Id").HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("exercise_id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("DescriptionEs").IsRequired().HasColumnType("text");
            b.Property<string>("Difficulty").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("Equipment").HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("ForceType").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("BodyPart").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Mechanic").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<double>("Met").HasColumnType("double precision");
            b.Property<string>("MinimumExperience").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("MovementPattern").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("NameEn").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("NameEs").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string[]>("InstructionsEs").IsRequired().HasColumnType("text[]");
            b.Property<string[]>("TipsEs").IsRequired().HasColumnType("text[]");
            b.Property<string[]>("PrimaryMuscles").IsRequired().HasColumnType("text[]");
            b.Property<string[]>("SecondaryMuscles").IsRequired().HasColumnType("text[]");
            b.Property<string[]>("SuitableGoals").IsRequired().HasColumnType("text[]");
            b.Property<string[]>("RequiredEquipment").IsRequired().HasColumnType("text[]");
            b.Property<int>("SeedVersion").HasColumnType("integer");
            b.Property<int>("SelectionPriority").HasColumnType("integer");
            b.Property<string>("Source").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<bool>("IsBodyweight").HasColumnType("boolean");
            b.Property<bool>("IsUnilateral").HasColumnType("boolean");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("MovementPattern");
            b.HasIndex("Source");
            b.ToTable("exercise_catalog");
        });
    }
}
