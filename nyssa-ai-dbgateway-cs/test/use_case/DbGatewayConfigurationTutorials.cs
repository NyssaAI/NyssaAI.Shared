using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.UseCaseTests.Fixtures;
using Xunit;

namespace NyssaAI.DbGateway.UseCaseTests;

public sealed class DbGatewayConfigurationTutorials : IDisposable
{
    private readonly DatabaseWorkspace _workspace = new();
    private readonly ConfigurationHost _configurationHost = new();

    public void Dispose()
    {
        _configurationHost.Dispose();
        _workspace.Dispose();
    }

    [Fact]
    public async Task AddNyssaDbGatewayAsync_EncryptedSettings_LoadsAndSwitchesFromSqliteToPostgreSql()
    {
        var store = _configurationHost.Open(_workspace.DirectoryPath);
        await store.SetDbGatewayConfigurationAsync(
            new DbGatewayConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = _workspace.ConnectionString("sqlite")
            },
            storageProvider: "aesLocal",
            cancellationToken: TestContext.Current.CancellationToken);

        var sqliteServices = new ServiceCollection();
        await sqliteServices.AddNyssaDbGatewayAsync(store, cancellationToken: TestContext.Current.CancellationToken);
        await using var sqliteContainer = sqliteServices.BuildServiceProvider();
        var sqliteGateway = sqliteContainer.GetRequiredService<IDbGateway>();
        Assert.Equal(DbDialect.Sqlite, sqliteGateway.Dialect);

        // Switch stored configuration to PostgreSQL
        await store.SetDbGatewayConfigurationAsync(
            new DbGatewayConfiguration
            {
                DatabaseProvider = "postgresql",
                ConnectionString = _workspace.ConnectionString("postgresql")
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var pgServices = new ServiceCollection();
        await pgServices.AddNyssaDbGatewayAsync(store, cancellationToken: TestContext.Current.CancellationToken);
        await using var pgContainer = pgServices.BuildServiceProvider();
        var pgGateway = pgContainer.GetRequiredService<IDbGateway>();
        Assert.Equal(DbDialect.PostgreSql, pgGateway.Dialect);
    }
}
