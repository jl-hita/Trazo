using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Trazo.Infrastructure.Persistence;
using Trazo.Domain.Identity;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
public sealed class TrazoDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

        modelBuilder.Entity("Trazo.Domain.Identity.TrazoUser", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)");
            b.Property<string>("NormalizedEmail").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<DateTimeOffset?>("EmailVerifiedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("PasswordHash").HasMaxLength(512).HasColumnType("character varying(512)");
            b.Property<string>("GoogleSubject").HasMaxLength(255).HasColumnType("character varying(255)");
            b.Property<string>("Role").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<bool>("TwoFactorEnabled").HasColumnType("boolean");
            b.Property<string>("ProtectedAuthenticatorKey").HasMaxLength(1024).HasColumnType("character varying(1024)");
            b.Property<long?>("LastTotpStep").HasColumnType("bigint");
            b.Property<int>("FailedLoginAttempts").HasColumnType("integer");
            b.Property<DateTimeOffset?>("LockoutEnd").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("LastLoginAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("DisabledAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("NormalizedEmail").IsUnique();
            b.HasIndex("GoogleSubject").IsUnique();
            b.HasIndex("Role");
            b.ToTable("users");
        });

        modelBuilder.Entity("Trazo.Domain.Identity.EmailActionToken", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("Purpose").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ConsumedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId", "Purpose", "ExpiresAt");
            b.ToTable("email_action_tokens");
        });

        modelBuilder.Entity("Trazo.Domain.Settings.AppSetting", b =>
        {
            b.Property<string>("Key").HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Value").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.Property<bool>("IsSecret").HasColumnType("boolean");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Key");
            b.ToTable("app_settings");
        });

        modelBuilder.Entity("Trazo.Domain.Identity.RefreshToken", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ReplacedByTokenHash").HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("DeviceName").HasMaxLength(120).HasColumnType("character varying(120)");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId", "ExpiresAt");
            b.ToTable("refresh_tokens");
        });

        modelBuilder.Entity("Trazo.Domain.Identity.AuthChallenge", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<int>("FailedAttempts").HasColumnType("integer");
            b.Property<DateTimeOffset?>("ConsumedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("ExpiresAt");
            b.HasIndex("UserId");
            b.ToTable("auth_challenges");
        });

        modelBuilder.Entity("Trazo.Domain.Identity.AuthRecoveryCode", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("CodeHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("UsedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("UserId", "CodeHash").IsUnique();
            b.ToTable("auth_recovery_codes");
        });

        modelBuilder.Entity("Trazo.Domain.Identity.EmailActionToken", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
        modelBuilder.Entity("Trazo.Domain.Identity.RefreshToken", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
        modelBuilder.Entity("Trazo.Domain.Identity.AuthChallenge", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
        modelBuilder.Entity("Trazo.Domain.Identity.AuthRecoveryCode", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

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
