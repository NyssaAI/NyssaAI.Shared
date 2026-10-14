using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;
using Xunit;

namespace NyssaAI.Repository.Tests.Migrations;

public sealed class MigrationRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateAsync_TransactionalTrigger_CreatesExecutesAndReverses(bool temporary)
    {
        var path = DatabasePath();
        try
        {
            var definitions = Definitions();
            definitions[1] = definitions[1] with
            {
                UpSql = "CREATE TABLE migration_second(id BIGINT); CREATE " + (temporary ? "TEMP " : "") +
                    "TRIGGER migration_insert AFTER INSERT ON migration_first BEGIN INSERT INTO migration_second VALUES (NEW.id); END; INSERT INTO migration_first VALUES (7)",
                DownSql = "DROP TABLE migration_second"
            };
            var catalog = MigrationCatalog.Create("sqlite", definitions, Pins());

            using (var services = CreateServices(path, catalog))
            {
                var runner = services.Services.GetRequiredService<IMigrationRunner>();
                await runner.MigrateAsync("2.0.0", TestContext.Current.CancellationToken);
                Assert.Equal(7, await ScalarAsync(services, "SELECT id FROM migration_second"));
                await runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken);
                Assert.Equal(0, await ScalarAsync(services, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('migration_first','migration_second','migration_insert')"));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateAsync_MissingCatalog_ReturnsFailureWithoutOpening(bool configureWithCallback)
    {
        var path = DatabasePath();
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        var configuration = new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = $"Data Source={path}" };
        var added = configureWithCallback ? services.AddNyssaRepository(() => configuration) : services.AddNyssaRepository(configuration);

        using (var host = builder.Build())
        {
            var container = host.Services;
            var result = await Assert.ThrowsAsync<InvalidOperationException>(() => container.GetRequiredService<IMigrationRunner>().MigrateAsync("1.0.0", TestContext.Current.CancellationToken));

            Assert.False(File.Exists(path));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddNyssaRepository_InvalidMigrationCatalogSet_LeavesServicesUnchanged(bool nullCatalog)
    {
        var services = new ServiceCollection();
        var catalog = Catalog();
        Assert.Throws<ArgumentException>(() => services.AddNyssaRepository(new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = "Data Source=unused.db" }, migrations: nullCatalog ? [null!] : [catalog, catalog]));
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateAsync_UpNoOpDown_CommitsMatchingSchemaAndHistory(bool buildMetadata)
    {
        var path = DatabasePath();
        try
        {
            using (var services = CreateServices(path, Catalog(buildMetadata: buildMetadata)))
            {
                var runner = services.Services.GetRequiredService<IMigrationRunner>();
                var release = buildMetadata ? "2.0.0-rc.1+build.5" : "2.0.0";
                var migrated = await runner.MigrateAsync(release, TestContext.Current.CancellationToken);
                var up = migrated;
                Assert.Equal(0, up.FromNumber);
                Assert.Equal(2, up.ToNumber);
                Assert.Equal(2, up.AppliedCount);
                var repeated = await runner.MigrateAsync(release, TestContext.Current.CancellationToken);
                var noOp = repeated;
                Assert.Equal(0, noOp.AppliedCount);
                Assert.Equal(2, await ScalarAsync(services, "SELECT COUNT(*) FROM nyssa_repository_migrations"));
                var downgraded = await runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken);
                var down = downgraded;
                Assert.Equal(2, down.AppliedCount);
                Assert.Equal(0, await ScalarAsync(services, "SELECT COUNT(*) FROM nyssa_repository_migrations"));
                Assert.Equal(0, await ScalarAsync(services, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('migration_first','migration_second')"));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("2.0.1")]
    [InlineData("2.0.0-rc.1")]
    [InlineData("2.0.0-rc.1+build.6")]
    public async Task MigrateAsync_MissingExactPin_DoesNotOpenDatabase(string release)
    {
        var path = DatabasePath();
        using (var services = CreateServices(path, Catalog(buildMetadata: true)))
        {
            var result = await Assert.ThrowsAsync<KeyNotFoundException>(() => services.Services.GetRequiredService<IMigrationRunner>().MigrateAsync(release, TestContext.Current.CancellationToken));

            Assert.False(File.Exists(path));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrateAsync_IrreversibleDowngrade_PreflightsEntireRange(bool nullDown)
    {
        var path = DatabasePath();
        try
        {
            var definitions = Definitions();
            definitions[0] = definitions[0] with { DownSql = nullDown ? null : "DROP TABLE migration_first" };
            if (!nullDown) definitions[1] = definitions[1] with { DownSql = null };
            using (var services = CreateServices(path, MigrationCatalog.Create("sqlite", definitions, Pins())))
            {
                var runner = services.Services.GetRequiredService<IMigrationRunner>();
                await runner.MigrateAsync("2.0.0", TestContext.Current.CancellationToken);
                await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken));
                Assert.Equal(2, await ScalarAsync(services, "SELECT COUNT(*) FROM nyssa_repository_migrations"));
                Assert.Equal(2, await ScalarAsync(services, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('migration_first','migration_second')"));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("DELETE FROM nyssa_repository_migrations WHERE number = 1")]
    [InlineData("UPDATE nyssa_repository_migrations SET fingerprint = 'changed' WHERE number = 1")]
    [InlineData("INSERT INTO nyssa_repository_migrations VALUES (3,'unknown')")]
    [InlineData("UPDATE nyssa_repository_migrations SET number = 1.5 WHERE number = 1")]
    [InlineData("UPDATE nyssa_repository_migrations SET number = NULL WHERE number = 1")]
    public async Task MigrateAsync_CorruptHistory_RejectsBeforeDdl(string corruption)
    {
        var path = DatabasePath();
        try
        {
            using (var services = CreateServices(path, Catalog()))
            {
                var runner = services.Services.GetRequiredService<IMigrationRunner>();
                await runner.MigrateAsync("2.0.0", TestContext.Current.CancellationToken);
                await ScalarAsync(services, corruption);
                await Assert.ThrowsAsync<InvalidOperationException>(() => runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken));
                Assert.Equal(2, await ScalarAsync(services, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('migration_first','migration_second')"));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("2.0.0")]
    public async Task MigrateAsync_FailedSecondStep_RollsBackAndRetrySucceeds(string release)
    {
        var path = DatabasePath();
        try
        {
            var definitions = Definitions();
            definitions[1] = definitions[1] with { UpSql = "INSERT INTO prerequisite VALUES (1); CREATE TABLE migration_second(id BIGINT)" };
            using (var services = CreateServices(path, MigrationCatalog.Create("sqlite", definitions, Pins())))
            {
                var runner = services.Services.GetRequiredService<IMigrationRunner>();
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => runner.MigrateAsync(release, TestContext.Current.CancellationToken));
                Assert.Equal(0, await ScalarAsync(services, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('migration_first','migration_second','nyssa_repository_migrations')"));
                await ScalarAsync(services, "CREATE TABLE prerequisite(id BIGINT)");
                await runner.MigrateAsync(release, TestContext.Current.CancellationToken);
                Assert.Equal(2, await ScalarAsync(services, "SELECT COUNT(*) FROM nyssa_repository_migrations"));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("1.0.0")]
    public async Task MigrateAsync_ConcurrentWriteLock_FailsImmediatelyAndRetries(string release)
    {
        var path = DatabasePath();
        try
        {
            using (var services = CreateServices(path, Catalog()))
            {
                var opened = await services.Services.GetRequiredService<IRepositoryProvider>().OpenConnectionAsync(TestContext.Current.CancellationToken);
                var owner = opened;
                await using (owner)
                {
                    await using (var transaction = await owner.BeginTransactionAsync(TestContext.Current.CancellationToken))
                    {
                        var started = Stopwatch.GetTimestamp();
                        var result = await Assert.ThrowsAsync<InvalidOperationException>(() => services.Services.GetRequiredService<IMigrationRunner>().MigrateAsync(release, TestContext.Current.CancellationToken));

                        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2));
                    }
                }
                await services.Services.GetRequiredService<IMigrationRunner>().MigrateAsync(release, TestContext.Current.CancellationToken);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("1.0.0")]
    public async Task MigrateAsync_PreCanceled_DoesNotOpenDatabase(string release)
    {
        var path = DatabasePath();
        using (var services = CreateServices(path, Catalog()))
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var result = await Assert.ThrowsAsync<OperationCanceledException>(() => services.Services.GetRequiredService<IMigrationRunner>().MigrateAsync(release, cancellation.Token));
                Assert.Equal(cancellation.Token, result.CancellationToken);
                Assert.False(File.Exists(path));
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_ReadOnlyDatabase_PreservesSqliteDiagnostics()
    {
        var path = DatabasePath();
        try
        {
            using (var writable = CreateServices(path, Catalog()))
            {
                await writable.Services.GetRequiredService<IMigrationRunner>().MigrateAsync("1.0.0", TestContext.Current.CancellationToken);
            }
            var services = new ServiceCollection();
            services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = $"Data Source={path};Mode=ReadOnly",
            }, migrations: [Catalog()]);
            await using (var container = services.BuildServiceProvider())
            {
                var failure = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
                    container.GetRequiredService<IMigrationRunner>().MigrateAsync("2.0.0", TestContext.Current.CancellationToken));
                Assert.Equal(8, failure.SqliteErrorCode);
            }
            using (var observer = CreateServices(path, Catalog()))
            {
                Assert.Equal(1, await ScalarAsync(observer, "SELECT COUNT(*) FROM nyssa_repository_migrations"));
                Assert.Equal(0, await ScalarAsync(observer, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'migration_second'"));
            }
        }
        finally { File.Delete(path); }
    }

    private static string DatabasePath() => Path.Combine(Path.GetTempPath(), $"nyssa-migration-unit-{Guid.NewGuid():N}.db");
    private static MigrationDefinition[] Definitions() =>
    [
        new() { Number = 1, UpSql = "CREATE TABLE migration_first(id BIGINT)", DownSql = "DROP TABLE migration_first" },
        new() { Number = 2, UpSql = "CREATE TABLE migration_second(id BIGINT)", DownSql = "DROP TABLE migration_second" }
    ];
    private static MigrationReleasePin[] Pins(bool buildMetadata = false) =>
    [
        new() { Release = "0.0.0", Number = 0 },
        new() { Release = "1.0.0", Number = 1 },
        new() { Release = buildMetadata ? "2.0.0-rc.1+build.5" : "2.0.0", Number = 2 }
    ];
    private static MigrationCatalog Catalog(bool buildMetadata = false) => MigrationCatalog.Create("sqlite", Definitions(), Pins(buildMetadata));
    private static IHost CreateServices(string path, MigrationCatalog catalog)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        _ = services.AddNyssaRepository(new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = $"Data Source={path}" }, migrations: [catalog]);
        return builder.Build();
    }
    private static async Task<long> ScalarAsync(IHost services, string sql)
    {
        var opened = await services.Services.GetRequiredService<IRepositoryProvider>().OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = opened;
        await using (connection)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            }
        }
    }
}
