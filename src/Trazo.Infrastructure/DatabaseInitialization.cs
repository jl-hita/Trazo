using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        await LogSeedDifferencesAsync(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<ExerciseImageSeeder>().SeedAsync();

        // Export the effective database catalog (including administrator edits), not just seed files.
        await scope.ServiceProvider.GetRequiredService<ExerciseCatalogArtifactExporter>().GenerateAsync();

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
                    EmailVerifiedAt = now,
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

    /// <summary>Says on startup that the catalog is not what the seed holds. It is a diagnosis: it never stops the API from starting.</summary>
    private static async Task LogSeedDifferencesAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<ExerciseSeedReportService>>();
        try
        {
            var report = await services.GetRequiredService<ExerciseSeedReportService>().BuildAsync();
            if (report.Differences.Count + report.MissingInDatabase.Count + report.Invalid.Count > 0)
                logger.LogWarning(
                    "Exercise catalog differs from the seed: {Engine} exercises with different planning metadata, {Other} with other differences, {Missing} missing and {Invalid} invalid. See GET /api/v1/admin/exercises/seed-report.",
                    report.Differences.Count(d => d.EngineFields.Count > 0), report.Differences.Count(d => d.EngineFields.Count == 0),
                    report.MissingInDatabase.Count, report.Invalid.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The exercise catalog could not be compared with the seed.");
        }
    }
}
