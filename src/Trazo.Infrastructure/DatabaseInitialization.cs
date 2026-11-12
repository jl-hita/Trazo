using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Trazo.Domain.Identity;

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

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var bootstrapEmail = configuration["BootstrapAdmin:Email"];
        var bootstrapPassword = configuration["BootstrapAdmin:Password"];
        if (!string.IsNullOrWhiteSpace(bootstrapEmail) || !string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            if (string.IsNullOrWhiteSpace(bootstrapEmail) || string.IsNullOrWhiteSpace(bootstrapPassword) ||
                bootstrapPassword.Length < 12 || !bootstrapPassword.Any(char.IsUpper) ||
                !bootstrapPassword.Any(char.IsLower) || !bootstrapPassword.Any(char.IsDigit))
                throw new InvalidOperationException("BootstrapAdmin requires a valid email and a password of at least 12 characters containing uppercase, lowercase and a number.");

            if (!await db.Users.AnyAsync())
            {
                var now = DateTimeOffset.UtcNow;
                var user = new TrazoUser
                {
                    Id = Guid.NewGuid(),
                    Email = bootstrapEmail.Trim(),
                    NormalizedEmail = bootstrapEmail.Trim().ToUpperInvariant(),
                    DisplayName = configuration["BootstrapAdmin:DisplayName"]?.Trim() is { Length: > 1 and <= 100 } name
                        ? name : "TRAZO Administrator",
                    Role = "superadmin",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                user.PasswordHash = scope.ServiceProvider
                    .GetRequiredService<PasswordHasher<TrazoUser>>()
                    .HashPassword(user, bootstrapPassword);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
        }
    }
}
