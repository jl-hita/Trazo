using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Training;
using Trazo.Domain.Identity;

namespace Trazo.Infrastructure.Persistence;

/// <summary>
/// Database context for TRAZO. The database is intentionally independent from DietoExpress.
/// </summary>
public sealed class TrazoDbContext(DbContextOptions<TrazoDbContext> options) : DbContext(options)
{
    public DbSet<ExerciseCatalogItem> ExerciseCatalog => Set<ExerciseCatalogItem>();
    public DbSet<TrazoUser> Users => Set<TrazoUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthChallenge> AuthChallenges => Set<AuthChallenge>();
    public DbSet<AuthRecoveryCode> AuthRecoveryCodes => Set<AuthRecoveryCode>();
    public DbSet<EmailActionToken> EmailActionTokens => Set<EmailActionToken>();
    public DbSet<Trazo.Domain.Billing.Subscription> Subscriptions => Set<Trazo.Domain.Billing.Subscription>();
    public DbSet<Trazo.Domain.Planning.TrainingPlan> TrainingPlans => Set<Trazo.Domain.Planning.TrainingPlan>();
    public DbSet<Trazo.Domain.Planning.TrainingPlanPhase> TrainingPlanPhases => Set<Trazo.Domain.Planning.TrainingPlanPhase>();
    public DbSet<Trazo.Domain.Planning.TrainingMesocycle> TrainingMesocycles => Set<Trazo.Domain.Planning.TrainingMesocycle>();
    public DbSet<Trazo.Domain.Planning.PlannedSession> PlannedSessions => Set<Trazo.Domain.Planning.PlannedSession>();
    public DbSet<Trazo.Domain.Planning.WorkoutLog> WorkoutLogs => Set<Trazo.Domain.Planning.WorkoutLog>();
    public DbSet<Trazo.Domain.Planning.WorkoutSetLog> WorkoutSetLogs => Set<Trazo.Domain.Planning.WorkoutSetLog>();
    public DbSet<Trazo.Domain.Planning.PlanAdaptation> PlanAdaptations => Set<Trazo.Domain.Planning.PlanAdaptation>();
    public DbSet<Trazo.Domain.Settings.AppSetting> AppSettings => Set<Trazo.Domain.Settings.AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrazoDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
