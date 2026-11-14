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
            b.Property<string>("Value").IsRequired().HasMaxLength(16000).HasColumnType("character varying(16000)");
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

        modelBuilder.Entity("Trazo.Domain.Billing.Subscription", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Source").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("ProductId").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("PurchaseTokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("ProtectedPurchaseToken").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("StoreState").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<string>("LatestOrderId").HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<DateTimeOffset>("StartedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("AutoRenewing").HasColumnType("boolean");
            b.Property<DateTimeOffset?>("AcknowledgedAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("IsTestPurchase").HasColumnType("boolean");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PurchaseTokenHash").IsUnique();
            b.HasIndex("UserId");
            b.HasIndex("Status", "ExpiresAt");
            b.ToTable("subscriptions");
        });
        modelBuilder.Entity("Trazo.Domain.Billing.Subscription", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.TrainingPlan", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Goal").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Experience").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Split").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<DateOnly>("StartDate").HasColumnType("date");
            b.Property<int>("TotalWeeks").HasColumnType("integer");
            b.Property<string[]>("TrainingDays").IsRequired().HasColumnType("text");
            b.Property<int>("MaxSessionMinutes").HasColumnType("integer");
            b.Property<string[]>("AvailableEquipment").IsRequired().HasColumnType("text");
            b.Property<string[]>("ExcludedExerciseIds").IsRequired().HasColumnType("text");
            b.Property<string[]>("Limitations").IsRequired().HasColumnType("text");
            b.Property<bool>("IncludeDeloadPhases").HasColumnType("boolean");
            b.Property<int>("DeloadEveryTrainingWeeks").HasColumnType("integer");
            b.Property<int>("Seed").HasColumnType("integer");
            b.Property<string>("EngineVersion").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("MacrocycleName").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<string>("MacrocycleNotes").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("UserId");
            b.HasIndex("UserId").IsUnique().HasFilter("\"Status\" = 'Active'").HasDatabaseName("ux_training_plans_one_active_per_user");
            b.ToTable("training_plans");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.TrainingPlan", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.TrainingPlanPhase", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<int>("SequenceNumber").HasColumnType("integer");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("PhaseType").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<DateOnly>("StartDate").HasColumnType("date");
            b.Property<DateOnly>("EndDate").HasColumnType("date");
            b.Property<string>("Objective").IsRequired().HasMaxLength(400).HasColumnType("character varying(400)");
            b.HasKey("Id");
            b.HasIndex("PlanId", "SequenceNumber").IsUnique();
            b.ToTable("training_plan_phases");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.TrainingPlanPhase", b =>
        {
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.TrainingMesocycle", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<int>("Sequence").HasColumnType("integer");
            b.Property<int>("PhaseSequenceNumber").HasColumnType("integer");
            b.Property<string>("PhaseType").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<bool>("IsDeload").HasColumnType("boolean");
            b.Property<int>("StartWeek").HasColumnType("integer");
            b.Property<int>("Weeks").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PlanId", "Sequence").IsUnique();
            b.ToTable("training_mesocycles");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.TrainingMesocycle", b =>
        {
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.PlannedSession", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<Guid>("MesocycleId").HasColumnType("uuid");
            b.Property<int>("WeekNumber").HasColumnType("integer");
            b.Property<DateOnly>("Date").HasColumnType("date");
            b.Property<string>("Title").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<int>("EstimatedDurationMinutes").HasColumnType("integer");
            b.Property<string>("PrescriptionJson").IsRequired().HasColumnType("text");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PlanId", "Date");
            b.HasIndex("MesocycleId");
            b.ToTable("planned_sessions");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.PlannedSession", b =>
        {
            b.HasOne("Trazo.Domain.Planning.TrainingMesocycle", null)
                .WithMany().HasForeignKey("MesocycleId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.NoAction).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.WorkoutLog", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<Guid>("PlannedSessionId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("LoggedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Outcome").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("NonCompletionReason").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Recovery").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<decimal?>("SessionRpe").HasPrecision(3, 1).HasColumnType("numeric(3,1)");
            b.Property<bool>("PainReported").HasColumnType("boolean");
            b.Property<string>("Notes").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.HasKey("Id");
            b.HasIndex("PlannedSessionId").IsUnique();
            b.HasIndex("PlanId", "LoggedAt");
            b.HasIndex("UserId");
            b.ToTable("workout_logs");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.WorkoutLog", b =>
        {
            b.HasOne("Trazo.Domain.Planning.PlannedSession", null)
                .WithMany().HasForeignKey("PlannedSessionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.WorkoutSetLog", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("WorkoutLogId").HasColumnType("uuid");
            b.Property<string>("ExerciseId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<int>("SetNumber").HasColumnType("integer");
            b.Property<decimal?>("WeightKg").HasPrecision(7, 2).HasColumnType("numeric(7,2)");
            b.Property<int?>("Repetitions").HasColumnType("integer");
            b.Property<decimal?>("Rir").HasPrecision(3, 1).HasColumnType("numeric(3,1)");
            b.Property<bool>("Completed").HasColumnType("boolean");
            b.Property<string>("SkippedReason").HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<bool>("PainReported").HasColumnType("boolean");
            b.HasKey("Id");
            b.HasIndex("WorkoutLogId", "ExerciseId", "SetNumber").IsUnique();
            b.HasIndex("ExerciseId");
            b.ToTable("workout_set_logs");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.WorkoutSetLog", b =>
        {
            b.HasOne("Trazo.Domain.Planning.WorkoutLog", null)
                .WithMany().HasForeignKey("WorkoutLogId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.PlanAdaptation", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<Guid?>("WorkoutLogId").HasColumnType("uuid");
            b.Property<string>("Kind").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string[]>("ExerciseIds").IsRequired().HasColumnType("text");
            b.Property<int>("SessionsChanged").HasColumnType("integer");
            b.Property<string>("Explanation").IsRequired().HasMaxLength(600).HasColumnType("character varying(600)");
            b.Property<DateTimeOffset>("AppliedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PlanId", "AppliedAt");
            b.ToTable("plan_adaptations");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.PlanAdaptation", b =>
        {
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.Cascade).IsRequired();
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
            b.Property<bool>("IsActive").HasColumnType("boolean");
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
