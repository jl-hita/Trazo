using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// The exercise seed over the real migrations on PostgreSQL: a new installation ends up holding exactly the seed, an edited
/// database comes back to it and a restart writes nothing. Runs only in the CI job "postgres" (TRAZO_TEST_POSTGRES).
/// </summary>
[Trait("Category", "Postgres")]
public sealed class PostgresExerciseSeedTests
{
    [ScenarioFact]
    public async Task TheSeedIsAppliedRestoredAndLeftAlone()
    {
        var admin = PostgresSettings.ConnectionString;
        var name = $"trazo_seed_{Guid.NewGuid():N}";
        await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"");
        try
        {
            var options = new DbContextOptionsBuilder<TrazoDbContext>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString).Options;
            await using (var db = new TrazoDbContext(options)) await db.Database.MigrateAsync();

            async Task<ExerciseSeedReport> SeedAndReportAsync()
            {
                await using (var db = new TrazoDbContext(options))
                    await new ExerciseCatalogSeeder(db, NullLogger<ExerciseCatalogSeeder>.Instance).SeedAsync();
                await using var read = new TrazoDbContext(options);
                return await new ExerciseSeedReportService(read).BuildAsync();
            }

            var fresh = await SeedAndReportAsync();
            Assert.Empty(fresh.MissingInDatabase);
            Assert.Empty(fresh.Differences);
            Assert.Empty(fresh.Invalid);

            await using (var db = new TrazoDbContext(options))
            {
                var bench = await db.ExerciseCatalog.SingleAsync(x => x.Id == "bench-press");
                bench.MinimumExperience = "Advanced";
                var german = await db.ExerciseCatalogTranslations.SingleAsync(x => x.ExerciseId == "bench-press" && x.LanguageCode == "de");
                german.Name = "Von Hand";
                await db.SaveChangesAsync();
            }
            Assert.Empty((await SeedAndReportAsync()).Differences);

            var stamps = await StampsAsync(options);
            await SeedAndReportAsync();
            Assert.Equal(stamps, await StampsAsync(options));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
    }

    private static async Task<List<string>> StampsAsync(DbContextOptions<TrazoDbContext> options)
    {
        await using var db = new TrazoDbContext(options);
        var exercises = await db.ExerciseCatalog.AsNoTracking().Select(x => new { x.Id, x.UpdatedAt }).ToListAsync();
        var texts = await db.ExerciseCatalogTranslations.AsNoTracking().Select(x => new { x.ExerciseId, x.LanguageCode, x.UpdatedAt }).ToListAsync();
        return exercises.Select(x => $"{x.Id} {x.UpdatedAt:O}").Concat(texts.Select(x => $"{x.ExerciseId} {x.LanguageCode} {x.UpdatedAt:O}"))
            .Order(StringComparer.Ordinal).ToList();
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync();
    }
}
