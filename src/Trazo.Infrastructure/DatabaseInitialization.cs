using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;

namespace Trazo.Infrastructure;

public static class DatabaseInitialization
{
    /// <summary>
    /// Applies all pending migrations and imports the versioned first-party exercise seed
    /// before the API starts accepting requests.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();

        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<ExerciseCatalogSeeder>();
        await seeder.SeedAsync();
    }
}
