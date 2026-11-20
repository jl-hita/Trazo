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
    public DbSet<ExerciseCatalogTranslation> ExerciseCatalogTranslations => Set<ExerciseCatalogTranslation>();
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
    public DbSet<Trazo.Domain.Planning.MuscleWeekObservation> MuscleWeekObservations => Set<Trazo.Domain.Planning.MuscleWeekObservation>();
    public DbSet<Trazo.Domain.Planning.LongitudinalDecision> LongitudinalDecisions => Set<Trazo.Domain.Planning.LongitudinalDecision>();
    public DbSet<Trazo.Domain.Planning.FrequencyProposal> FrequencyProposals => Set<Trazo.Domain.Planning.FrequencyProposal>();
    public DbSet<Trazo.Domain.Planning.PlanReviewRequest> PlanReviewRequests => Set<Trazo.Domain.Planning.PlanReviewRequest>();
    public DbSet<Trazo.Domain.Planning.PlanReviewMessage> PlanReviewMessages => Set<Trazo.Domain.Planning.PlanReviewMessage>();
    public DbSet<Trazo.Domain.Settings.AppSetting> AppSettings => Set<Trazo.Domain.Settings.AppSetting>();
    public DbSet<Trazo.Domain.Push.DeviceToken> DeviceTokens => Set<Trazo.Domain.Push.DeviceToken>();
    public DbSet<Trazo.Domain.Waitlist.WaitlistEntry> WaitlistEntries => Set<Trazo.Domain.Waitlist.WaitlistEntry>();
    public DbSet<Trazo.Domain.Support.ErrorTicket> ErrorTickets => Set<Trazo.Domain.Support.ErrorTicket>();
    public DbSet<Trazo.Domain.Analytics.PaywallView> PaywallViews => Set<Trazo.Domain.Analytics.PaywallView>();

    public DbSet<Trazo.Domain.Growth.PremiumGrant> PremiumGrants => Set<Trazo.Domain.Growth.PremiumGrant>();
    public DbSet<Trazo.Domain.Growth.ReferralCode> ReferralCodes => Set<Trazo.Domain.Growth.ReferralCode>();
    public DbSet<Trazo.Domain.Growth.ReferralRelationship> ReferralRelationships => Set<Trazo.Domain.Growth.ReferralRelationship>();
    public DbSet<Trazo.Domain.Growth.GrowthEvent> GrowthEvents => Set<Trazo.Domain.Growth.GrowthEvent>();

    public DbSet<Trazo.Domain.Marketing.MarketingCampaign> MarketingCampaigns => Set<Trazo.Domain.Marketing.MarketingCampaign>();
    public DbSet<Trazo.Domain.Marketing.CampaignAttribution> CampaignAttributions => Set<Trazo.Domain.Marketing.CampaignAttribution>();
    public DbSet<Trazo.Domain.Marketing.CampaignDailyStat> CampaignDailyStats => Set<Trazo.Domain.Marketing.CampaignDailyStat>();
    public DbSet<Trazo.Domain.Marketing.CampaignSpend> CampaignSpends => Set<Trazo.Domain.Marketing.CampaignSpend>();
    public DbSet<Trazo.Domain.Marketing.StoreDownloads> StoreDownloads => Set<Trazo.Domain.Marketing.StoreDownloads>();

    public DbSet<Trazo.Domain.Fiscal.FiscalEntry> FiscalEntries => Set<Trazo.Domain.Fiscal.FiscalEntry>();
    public DbSet<Trazo.Domain.Fiscal.FiscalClosure> FiscalClosures => Set<Trazo.Domain.Fiscal.FiscalClosure>();
    public DbSet<Trazo.Domain.Fiscal.FiscalAuditEntry> FiscalAudit => Set<Trazo.Domain.Fiscal.FiscalAuditEntry>();

    // Marking a property as a concurrency token only makes EF compare it; it never advances it. The counter of a planned session is advanced here,
    // in the one place every save goes through (every SaveChanges overload ends in one of these two), so no write
    // path can modify a session without bumping it, and nothing has to remember to do it.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AdvanceSessionVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AdvanceSessionVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AdvanceSessionVersions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<Trazo.Domain.Planning.PlannedSession>().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Property(s => s.Version);
            // The next value is derived from the one EF will compare against (the one read from the database), never from the current value, so
            // saving twice with the same instance, or a value assigned by mistake, cannot skip or repeat a number.
            version.CurrentValue = version.OriginalValue + 1;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrazoDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
