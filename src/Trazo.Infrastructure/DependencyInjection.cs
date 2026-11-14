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
        services.AddScoped<Trazo.Infrastructure.Planning.ITrainingPlanService, Trazo.Infrastructure.Planning.TrainingPlanService>();
        services.AddScoped<ExerciseCatalogSeeder>();
        services.AddScoped<ExerciseImageSeeder>();
        services.AddScoped<ExerciseLibraryExporter>();
        services.AddScoped<ExerciseLibraryImportService>();
        services.AddScoped<AppLogReader>();

        return services;
    }
}
