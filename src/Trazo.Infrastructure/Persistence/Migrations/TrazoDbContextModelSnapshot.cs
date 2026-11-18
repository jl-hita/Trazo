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
            b.Property<string>("TimeZoneId").HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("Language").HasMaxLength(5).HasColumnType("character varying(5)");
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

        modelBuilder.Entity("Trazo.Domain.Push.DeviceToken", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Token").IsRequired().HasMaxLength(1024).HasColumnType("character varying(1024)");
            b.Property<string>("Platform").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("LastSeenAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Token").IsUnique();
            b.HasIndex("UserId");
            b.ToTable("device_tokens");
        });
        modelBuilder.Entity("Trazo.Domain.Push.DeviceToken", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Waitlist.WaitlistEntry", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("character varying(254)");
            b.Property<string>("Language").IsRequired().HasMaxLength(2).HasColumnType("character varying(2)");
            b.Property<DateTimeOffset>("ConsentAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ConfirmedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ConfirmTokenHash").HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset?>("ConfirmTokenExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("InvitedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Email").IsUnique();
            b.HasIndex("CreatedAt");
            b.HasIndex("ConfirmTokenHash");
            b.ToTable("waitlist_entries");
        });

        modelBuilder.Entity("Trazo.Domain.Analytics.PaywallView", b =>
        {
            b.Property<DateOnly>("Day").HasColumnType("date");
            b.Property<string>("Source").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.Property<int>("Views").HasColumnType("integer");
            b.HasKey("Day", "Source");
            b.ToTable("paywall_views");
        });

        modelBuilder.Entity("Trazo.Domain.Support.ErrorTicket", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Fingerprint").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<string>("Source").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.Property<string>("AppVersion").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("ErrorType").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("StackTrace").IsRequired().HasMaxLength(16000).HasColumnType("character varying(16000)");
            b.Property<string>("OriginalStackTrace").HasMaxLength(16000).HasColumnType("character varying(16000)");
            b.Property<int>("Occurrences").HasColumnType("integer");
            b.Property<DateTimeOffset>("FirstSeenAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("LastSeenAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.HasKey("Id");
            b.HasIndex("Fingerprint").IsUnique();
            b.HasIndex("LastSeenAt");
            b.ToTable("error_tickets");
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
            b.Property<DateTimeOffset?>("PaidAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PurchaseTokenHash").IsUnique();
            b.HasIndex("Status", "ExpiresAt");
            b.HasIndex("UserId");
            b.ToTable("subscriptions");
        });
        modelBuilder.Entity("Trazo.Domain.Billing.Subscription", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Planning.PlanReviewRequest", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("PlanId").HasColumnType("uuid");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<Guid?>("AssignedToUserId").HasColumnType("uuid");
            b.Property<DateTimeOffset?>("AssignedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ReviewedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("UserLastReadAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Status", "UpdatedAt");
            b.HasIndex("UserId", "CreatedAt");
            b.HasIndex("AssignedToUserId");
            b.HasIndex("PlanId").IsUnique().HasFilter("\"Status\" <> 'Reviewed'");
            b.ToTable("plan_review_requests");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.PlanReviewRequest", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Planning.TrainingPlan", null)
                .WithMany().HasForeignKey("PlanId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("AssignedToUserId").OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity("Trazo.Domain.Planning.PlanReviewMessage", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("RequestId").HasColumnType("uuid");
            b.Property<string>("Author").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<Guid?>("AuthorUserId").HasColumnType("uuid");
            b.Property<string>("Body").IsRequired().HasMaxLength(2000).HasColumnType("character varying(2000)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("RequestId", "CreatedAt");
            b.HasIndex("AuthorUserId");
            b.ToTable("plan_review_messages");
        });
        modelBuilder.Entity("Trazo.Domain.Planning.PlanReviewMessage", b =>
        {
            b.HasOne("Trazo.Domain.Planning.PlanReviewRequest", null)
                .WithMany().HasForeignKey("RequestId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("AuthorUserId").OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity("Trazo.Domain.Planning.TrainingPlan", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Origin").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Goal").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Experience").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Split").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<DateOnly>("StartDate").HasColumnType("date");
            b.Property<int>("TotalWeeks").HasColumnType("integer");
            b.Property<string>("SchedulingMode").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<int>("SessionsPerPeriod").HasColumnType("integer");
            b.Property<int>("ScheduleShiftDays").HasColumnType("integer");
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
            b.Property<int>("StartPeriod").HasColumnType("integer");
            b.Property<int>("EndPeriod").HasColumnType("integer");
            b.Property<DateOnly?>("StartDate").HasColumnType("date");
            b.Property<DateOnly?>("EndDate").HasColumnType("date");
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
            b.Property<DateOnly?>("Date").HasColumnType("date");
            b.Property<int>("SequenceIndex").HasColumnType("integer");
            b.Property<string>("Title").IsRequired().HasMaxLength(160).HasColumnType("character varying(160)");
            b.Property<int>("EstimatedDurationMinutes").HasColumnType("integer");
            b.Property<string>("PrescriptionJson").IsRequired().HasColumnType("text");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PlanId", "Date");
            b.HasIndex("PlanId", "SequenceIndex").IsUnique();
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
            b.Property<DateTimeOffset>("PerformedAt").HasColumnType("timestamp with time zone");
            b.Property<DateOnly>("PerformedOn").HasColumnType("date");
            b.Property<string>("TimeZoneId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<Guid?>("ClientRequestId").HasColumnType("uuid");
            b.Property<string>("Outcome").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("NonCompletionReason").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Recovery").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<decimal?>("SessionRpe").HasPrecision(3, 1).HasColumnType("numeric(3,1)");
            b.Property<bool>("PainReported").HasColumnType("boolean");
            b.Property<string>("Notes").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("ExerciseFeedbackJson").HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.HasKey("Id");
            b.HasIndex("PlannedSessionId").IsUnique();
            b.HasIndex("PlanId", "LoggedAt");
            b.HasIndex("PlanId", "PerformedAt");
            b.HasIndex("UserId", "ClientRequestId").IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
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
            b.Property<DateTimeOffset?>("EvidenceAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("PlanId", "AppliedAt");
            b.HasIndex("PlanId", "EvidenceAt").IsUnique().HasFilter("\"Kind\" = 'reentry'");
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

        modelBuilder.Entity("Trazo.Domain.Training.ExerciseCatalogTranslation", b =>
        {
            b.Property<string>("ExerciseId").HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("exercise_id");
            b.Property<string>("LanguageCode").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<string>("Name").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Description").HasColumnType("text");
            b.Property<string[]>("Instructions").HasColumnType("text[]");
            b.Property<string[]>("Tips").HasColumnType("text[]");
            b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasMaxLength(16).HasDefaultValue("draft").HasColumnType("character varying(16)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("ExerciseId", "LanguageCode");
            b.HasIndex("LanguageCode", "Status");
            b.ToTable("exercise_catalog_translations", t =>
            {
                t.HasCheckConstraint("CK_exercise_catalog_translations_language", "\"LanguageCode\" ~ '^[a-z]{2,3}(-[A-Z]{2})?$'");
                t.HasCheckConstraint("CK_exercise_catalog_translations_status", "\"Status\" IN ('draft', 'review', 'published')");
            });
        });

        modelBuilder.Entity("Trazo.Domain.Training.ExerciseCatalogTranslation", b =>
        {
            b.HasOne("Trazo.Domain.Training.ExerciseCatalogItem", null)
                .WithMany().HasForeignKey("ExerciseId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Growth.PremiumGrant", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Kind").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("SourceKey").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<int>("Days").HasColumnType("integer");
            b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.Property<DateTimeOffset>("GrantedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("StartsAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("EndsAt").HasColumnType("timestamp with time zone");
            b.Property<Guid?>("OriginUserId").HasColumnType("uuid");
            b.Property<Guid?>("OriginSubscriptionId").HasColumnType("uuid");
            b.Property<Guid?>("DeferredSubscriptionId").HasColumnType("uuid");
            b.Property<DateTimeOffset?>("GoogleExpiryBefore").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("GoogleExpiryAfter").HasColumnType("timestamp with time zone");
            b.Property<string>("GoogleOrderBefore").HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<DateTimeOffset?>("AppliedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("VoidedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("VoidReason").HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<string>("VoidedBy").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<int>("DeferAttempts").HasColumnType("integer");
            b.Property<string>("LastDeferError").HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Kind", "SourceKey").IsUnique();
            b.HasIndex("Status");
            b.HasIndex("UserId", "Status");
            b.ToTable("premium_grants");
        });
        modelBuilder.Entity("Trazo.Domain.Growth.PremiumGrant", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Growth.ReferralCode", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("Code").IsRequired().HasMaxLength(12).HasColumnType("character varying(12)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Code").IsUnique();
            b.HasIndex("UserId").IsUnique();
            b.ToTable("referral_codes");
        });
        modelBuilder.Entity("Trazo.Domain.Growth.ReferralCode", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Growth.ReferralRelationship", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("ReferrerUserId").HasColumnType("uuid");
            b.Property<Guid>("ReferredUserId").HasColumnType("uuid");
            b.Property<string>("Code").IsRequired().HasMaxLength(12).HasColumnType("character varying(12)");
            b.Property<string>("Source").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.Property<DateTimeOffset>("AttributedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("AppVersion").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<Guid?>("ConversionSubscriptionId").HasColumnType("uuid");
            b.Property<string>("ConversionPlan").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<DateTimeOffset?>("ConvertedAt").HasColumnType("timestamp with time zone");
            b.Property<Guid?>("RewardGrantId").HasColumnType("uuid");
            b.Property<int?>("RewardDays").HasColumnType("integer");
            b.Property<string>("Incident").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Code");
            b.HasIndex("ReferredUserId").IsUnique();
            b.HasIndex("Status");
            b.HasIndex("ReferrerUserId", "Status");
            b.ToTable("referral_relationships");
        });
        modelBuilder.Entity("Trazo.Domain.Growth.ReferralRelationship", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("ReferrerUserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("ReferredUserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Growth.GrowthEvent", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<DateTimeOffset>("OccurredAt").HasColumnType("timestamp with time zone");
            b.Property<Guid?>("UserId").HasColumnType("uuid");
            b.Property<string>("Channel").HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("Reference").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Plan").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("AppVersion").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("Variant").HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("DedupeKey").HasMaxLength(120).HasColumnType("character varying(120)");
            b.HasKey("Id");
            b.HasIndex("DedupeKey").IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
            b.HasIndex("Name", "OccurredAt");
            b.HasIndex("Reference", "Name");
            b.HasIndex("UserId");
            b.ToTable("growth_events");
        });
        modelBuilder.Entity("Trazo.Domain.Growth.GrowthEvent", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity("Trazo.Domain.Fiscal.FiscalEntry", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Kind").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<DateOnly>("Date").HasColumnType("date");
            b.Property<string>("Period").IsRequired().HasMaxLength(7).HasColumnType("character varying(7)");
            b.Property<string>("Concept").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Counterparty").HasMaxLength(150).HasColumnType("character varying(150)");
            b.Property<string>("CounterpartyTaxId").HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("CounterpartyCountry").HasMaxLength(2).HasColumnType("character varying(2)");
            b.Property<string>("InvoiceNumber").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<string>("Category").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<decimal>("Base").HasPrecision(14, 2).HasColumnType("numeric(14,2)");
            b.Property<string>("VatTreatment").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<decimal>("VatRatePct").HasPrecision(5, 2).HasColumnType("numeric(5,2)");
            b.Property<decimal>("VatAmount").HasPrecision(14, 2).HasColumnType("numeric(14,2)");
            b.Property<decimal>("WithholdingPct").HasPrecision(5, 2).HasColumnType("numeric(5,2)");
            b.Property<decimal>("WithholdingAmount").HasPrecision(14, 2).HasColumnType("numeric(14,2)");
            b.Property<decimal>("DeductiblePct").HasPrecision(5, 2).HasColumnType("numeric(5,2)");
            b.Property<decimal>("Total").HasPrecision(14, 2).HasColumnType("numeric(14,2)");
            b.Property<string>("Source").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Notes").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("CreatedBy").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Period", "Kind", "Date");
            b.ToTable("fiscal_entries");
        });

        modelBuilder.Entity("Trazo.Domain.Fiscal.FiscalClosure", b =>
        {
            b.Property<string>("Period").HasMaxLength(7).HasColumnType("character varying(7)");
            b.Property<DateTimeOffset>("ClosedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("ClosedBy").IsRequired().HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<int>("EntryCount").HasColumnType("integer");
            b.Property<string>("SnapshotJson").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.HasKey("Period");
            b.ToTable("fiscal_closures");
        });

        modelBuilder.Entity("Trazo.Domain.Fiscal.FiscalAuditEntry", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Period").IsRequired().HasMaxLength(7).HasColumnType("character varying(7)");
            b.Property<string>("Action").IsRequired().HasMaxLength(12).HasColumnType("character varying(12)");
            b.Property<string>("Actor").IsRequired().HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<string>("Reason").HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<DateTimeOffset>("At").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("At");
            b.ToTable("fiscal_audit");
        });

        modelBuilder.Entity("Trazo.Domain.Marketing.MarketingCampaign", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Slug").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("Channel").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Status").IsRequired().HasMaxLength(12).HasColumnType("character varying(12)");
            b.Property<DateOnly?>("StartsOn").HasColumnType("date");
            b.Property<DateOnly?>("EndsOn").HasColumnType("date");
            b.Property<string>("Notes").HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("CreatedBy").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("Slug").IsUnique();
            b.ToTable("marketing_campaigns");
        });

        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignAttribution", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("CampaignId").HasColumnType("uuid");
            b.Property<string>("Source").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTimeOffset>("AttributedAt").HasColumnType("timestamp with time zone");
            b.HasKey("UserId");
            b.HasIndex("CampaignId");
            b.ToTable("campaign_attributions");
        });
        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignAttribution", b =>
        {
            b.HasOne("Trazo.Domain.Identity.TrazoUser", null)
                .WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne("Trazo.Domain.Marketing.MarketingCampaign", null)
                .WithMany().HasForeignKey("CampaignId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignDailyStat", b =>
        {
            b.Property<Guid>("CampaignId").HasColumnType("uuid");
            b.Property<DateOnly>("Day").HasColumnType("date");
            b.Property<int>("Clicks").HasColumnType("integer");
            b.Property<int>("Installs").HasColumnType("integer");
            b.HasKey("CampaignId", "Day");
            b.ToTable("campaign_daily_stats");
        });
        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignDailyStat", b =>
        {
            b.HasOne("Trazo.Domain.Marketing.MarketingCampaign", null)
                .WithMany().HasForeignKey("CampaignId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignSpend", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("CampaignId").HasColumnType("uuid");
            b.Property<DateOnly>("Day").HasColumnType("date");
            b.Property<decimal>("Amount").HasPrecision(12, 2).HasColumnType("numeric(12,2)");
            b.Property<string>("Note").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("CreatedBy").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("CampaignId", "Day");
            b.ToTable("campaign_spend");
        });
        modelBuilder.Entity("Trazo.Domain.Marketing.CampaignSpend", b =>
        {
            b.HasOne("Trazo.Domain.Marketing.MarketingCampaign", null)
                .WithMany().HasForeignKey("CampaignId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Trazo.Domain.Marketing.StoreDownloads", b =>
        {
            b.Property<DateOnly>("Week").HasColumnType("date");
            b.Property<int>("Downloads").HasColumnType("integer");
            b.Property<string>("Note").HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("UpdatedBy").HasMaxLength(60).HasColumnType("character varying(60)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Week");
            b.ToTable("store_downloads");
        });
    }
}
