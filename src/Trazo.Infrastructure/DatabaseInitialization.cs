using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure;

public static class DatabaseInitialization
{
    /// <summary>
    /// Creates the configured database when the PostgreSQL credentials permit it,
    /// then applies every pending EF Core migration before the API starts serving requests.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();

        await db.Database.MigrateAsync();
    }
}
