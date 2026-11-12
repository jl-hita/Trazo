using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;

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

        services.AddDbContext<TrazoDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure()));

        services.AddScoped<ExerciseCatalogSeeder>();
        services.AddScoped<ExerciseLibraryImportService>();

        return services;
    }
}
