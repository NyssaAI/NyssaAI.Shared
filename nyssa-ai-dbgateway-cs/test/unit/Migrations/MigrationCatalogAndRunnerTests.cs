using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.Migrations;
using Xunit;

namespace NyssaAI.DbGateway.Tests.Migrations;

public sealed class MigrationCatalogAndRunnerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("nyssa-dbgateway-migrations-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task MigrateAsync_PairedAndUniversalSteps_UpgradesAndDowngradesSchema()
    {
        var dbPath = Path.Join(_directory, "migrations.db");
        var catalog = MigrationCatalog.Create(
            steps:
            [
                MigrationStep.Paired(
                    number: 1,
                    sqliteUpSql: "CREATE TABLE items (id TEXT PRIMARY KEY, active INTEGER NOT NULL)",
                    postgresUpSql: "CREATE TABLE items (id UUID PRIMARY KEY, active BOOLEAN NOT NULL)",
                    sqliteDownSql: "DROP TABLE items",
                    postgresDownSql: "DROP TABLE items"),
                MigrationStep.Universal(
                    number: 2,
                    upSql: "ALTER TABLE items ADD COLUMN note TEXT",
                    downSql: "ALTER TABLE items DROP COLUMN note")
            ],
            releases:
            [
                new() { Release = "0.0.0", Number = 0 },
                new() { Release = "1.0.0", Number = 1 },
                new() { Release = "1.1.0", Number = 2 }
            ]);

        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = $"Data Source={dbPath}"
            },
            migrations: catalog);

        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IMigrationRunner>();

        var upgrade = await runner.MigrateAsync("1.1.0", TestContext.Current.CancellationToken);
        Assert.Equal(0, upgrade.FromNumber);
        Assert.Equal(2, upgrade.ToNumber);
        Assert.Equal(2, upgrade.AppliedCount);

        var idempotent = await runner.MigrateAsync("1.1.0", TestContext.Current.CancellationToken);
        Assert.Equal(2, idempotent.FromNumber);
        Assert.Equal(2, idempotent.ToNumber);
        Assert.Equal(0, idempotent.AppliedCount);

        var downgrade = await runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken);
        Assert.Equal(2, downgrade.FromNumber);
        Assert.Equal(0, downgrade.ToNumber);
        Assert.Equal(2, downgrade.AppliedCount);
    }

    [Fact]
    public void Create_NonTransactionalSqlOrInvalidReleasePin_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            MigrationCatalog.Create(
                steps: [MigrationStep.Universal(1, "BEGIN TRANSACTION; CREATE TABLE t (id INT); COMMIT;")],
                releases: [new() { Release = "1.0.0", Number = 1 }]));

        Assert.Throws<ArgumentException>(() =>
            MigrationCatalog.Create(
                steps: [MigrationStep.Universal(1, "CREATE TABLE t (id INT)")],
                releases: [new() { Release = "v1.0", Number = 1 }]));
    }
}
