using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Trazo.Domain.Identity;
using System.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Logging;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Trazo");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The PostgreSQL connection string 'ConnectionStrings:Trazo' is required.");
        }

        // No EnableRetryOnFailure: the retrying execution strategy rejects the user-initiated
        // transactions used by the seeder and the authentication/account endpoints. PostgreSQL
        // runs on the API host, so retries on transient faults are not needed.
        services.AddDbContext<TrazoDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<PasswordHasher<TrazoUser>>();

        var dataProtection = services.AddDataProtection().SetApplicationName("TRAZO");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            Directory.CreateDirectory(keysPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
        services.AddRuntimeSettings();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPlayBillingClient, GooglePlayBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<Trazo.Infrastructure.Growth.IPremiumGrantService, Trazo.Infrastructure.Growth.PremiumGrantService>();
        services.AddScoped<Trazo.Infrastructure.Growth.IGrowthEvents, Trazo.Infrastructure.Growth.GrowthEventRecorder>();
        services.AddScoped<Trazo.Infrastructure.Growth.ISignupTrialService, Trazo.Infrastructure.Growth.SignupTrialService>();
        services.AddScoped<ISubscriptionObserver, Trazo.Infrastructure.Growth.GrowthSubscriptionObserver>();
        services.AddScoped<Trazo.Infrastructure.Growth.IReferralService, Trazo.Infrastructure.Growth.ReferralService>();
        services.AddScoped<Trazo.Infrastructure.Growth.IReferralRewardService, Trazo.Infrastructure.Growth.ReferralRewardService>();
        services.AddScoped<ISubscriptionObserver, Trazo.Infrastructure.Growth.ReferralSubscriptionObserver>();
        services.AddScoped<Trazo.Infrastructure.Growth.ReferralReconciler>();
        services.AddScoped<Trazo.Infrastructure.Growth.IGrowthAnalytics, Trazo.Infrastructure.Growth.GrowthAnalytics>();
        services.AddScoped<Trazo.Application.Localization.RequestLanguage>();
        services.AddScoped<Trazo.Infrastructure.Planning.IUserTimeZone, Trazo.Infrastructure.Planning.UserTimeZone>();
        services.AddScoped<Trazo.Infrastructure.Accounts.IAccountService, Trazo.Infrastructure.Accounts.AccountService>();
        services.AddScoped<Trazo.Infrastructure.Planning.ITrainingPlanService, Trazo.Infrastructure.Planning.TrainingPlanService>();
        services.AddScoped<ExerciseCatalogSeeder>();
        services.AddScoped<ExerciseImageSeeder>();
        services.AddScoped<ExerciseLibraryExporter>();
        services.AddScoped<ExerciseLibraryImportService>();
        services.AddScoped<AppLogReader>();

        return services;
    }
}
