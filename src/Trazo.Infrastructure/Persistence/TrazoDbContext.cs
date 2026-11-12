using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Training;

namespace Trazo.Infrastructure.Persistence;

/// <summary>
/// Database context for TRAZO. The database is intentionally independent from DietoExpress.
/// </summary>
public sealed class TrazoDbContext(DbContextOptions<TrazoDbContext> options) : DbContext(options)
{
    public DbSet<ExerciseCatalogItem> ExerciseCatalog => Set<ExerciseCatalogItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrazoDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
