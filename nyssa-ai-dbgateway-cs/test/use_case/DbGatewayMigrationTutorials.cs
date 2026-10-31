using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.Migrations;
using NyssaAI.DbGateway.Providers;
using NyssaAI.DbGateway.UseCaseTests.Fixtures;
using Xunit;

namespace NyssaAI.DbGateway.UseCaseTests;

[Collection("Database")]
public sealed class DbGatewayMigrationTutorials
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task MigrateAsync_UpgradeIdempotentAndDowngrade_SucceedsOnBothProviders(string providerName)
    {
        using var workspace = new DatabaseWorkspace();
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = providerName,
                ConnectionString = workspace.ConnectionString(providerName)
            },
            migrations: DatabaseWorkspace.TaskCatalog);

        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IDbProvider>();
        await workspace.ResetSchemaAsync(provider, TestContext.Current.CancellationToken);

        var runner = container.GetRequiredService<IMigrationRunner>();

        var first = await runner.MigrateAsync("1.0.0", TestContext.Current.CancellationToken);
        Assert.Equal(0, first.FromNumber);
        Assert.Equal(1, first.ToNumber);
        Assert.Equal(1, first.AppliedCount);

        var second = await runner.MigrateAsync("1.1.0", TestContext.Current.CancellationToken);
        Assert.Equal(1, second.FromNumber);
        Assert.Equal(2, second.ToNumber);
        Assert.Equal(1, second.AppliedCount);

        var downgrade = await runner.MigrateAsync("0.0.0", TestContext.Current.CancellationToken);
        Assert.Equal(2, downgrade.FromNumber);
        Assert.Equal(0, downgrade.ToNumber);
        Assert.Equal(2, downgrade.AppliedCount);
    }
}
