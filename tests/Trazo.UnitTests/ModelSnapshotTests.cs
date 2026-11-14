using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests;

public sealed class ModelSnapshotTests
{
    [Fact]
    public void MigrationSnapshot_MatchesCurrentModel()
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>()
            .UseNpgsql("Host=localhost;Database=model_check;Username=x;Password=x")
            .Options;
        using var db = new TrazoDbContext(options);
        var services = ((IInfrastructure<IServiceProvider>)db).Instance;

        var snapshot = services.GetRequiredService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);
        var snapshotModel = services.GetRequiredService<IModelRuntimeInitializer>()
            .Initialize(snapshot!.Model);
        var currentModel = services.GetRequiredService<IDesignTimeModel>().Model;

        var differences = services.GetRequiredService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        Assert.True(differences.Count == 0,
            "Model has pending changes not covered by migrations: " +
            string.Join("; ", differences.Select(Describe)));
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation o => $"AddColumn {o.Table}.{o.Name}",
        AlterColumnOperation o => $"AlterColumn {o.Table}.{o.Name} ({o.ClrType.Name}, nullable={o.IsNullable}, type={o.ColumnType})",
        DropColumnOperation o => $"DropColumn {o.Table}.{o.Name}",
        CreateTableOperation o => $"CreateTable {o.Name}",
        DropTableOperation o => $"DropTable {o.Name}",
        CreateIndexOperation o => $"CreateIndex {o.Table}.{o.Name}",
        DropIndexOperation o => $"DropIndex {o.Table}.{o.Name}",
        AddForeignKeyOperation o => $"AddForeignKey {o.Table}.{o.Name}",
        DropForeignKeyOperation o => $"DropForeignKey {o.Table}.{o.Name}",
        AddPrimaryKeyOperation o => $"AddPrimaryKey {o.Table}.{o.Name}",
        DropPrimaryKeyOperation o => $"DropPrimaryKey {o.Table}.{o.Name}",
        _ => operation.GetType().Name
    };
}

public sealed class SubscriptionMigrationTests
{
    [Fact]
    public void SubscriptionsMigration_IsDiscovered_AndGeneratesTheTableAndUniqueTokenIndex()
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>()
            .UseNpgsql("Host=localhost;Database=model_check;Username=x;Password=x").Options;
        using var db = new TrazoDbContext(options);
        var migrator = db.GetService<IMigrator>();

        var script = migrator.GenerateScript("20261113140000_AppLogs", "20261114100000_Subscriptions");

        Assert.Contains("CREATE TABLE subscriptions", script);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_subscriptions_PurchaseTokenHash\"", script);
        Assert.Contains("REFERENCES users", script);
    }
}
