using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryMigrationAotTutorials
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task MigrateAsync_DeployThenRollBackRelease_MatchesPinnedSchemaAsync(string databaseProvider)
    {
        // Arrange
        using var database = new DatabaseWorkspace();
        using var host = new RepositoryHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        Result registered = Result.Success();

        // Act
        // The application compiles both directions of each migration and pins releases to schema numbers.
        var catalogResult = MigrationCatalog.Create(databaseProvider,
        [
            new() { Number = 1, UpSql = "CREATE TABLE migration_tutorial_tasks(id BIGINT PRIMARY KEY, title TEXT NOT NULL)", DownSql = "DROP TABLE migration_tutorial_tasks" },
            new() { Number = 2, UpSql = "CREATE TABLE migration_tutorial_outbox(id BIGINT PRIMARY KEY, payload TEXT NOT NULL)", DownSql = "DROP TABLE migration_tutorial_outbox" }
        ],
        [
            new() { Release = "0.0.0", Number = 0 },
            new() { Release = "1.0.0", Number = 1 },
            new() { Release = "2.0.0-rc.1+linux.5", Number = 2 }
        ]);
        var catalog = catalogResult.Unwrap(); // Invalid catalogs prevent startup; Unwrap throws on failure.
        host.Register(services =>
        {
            registered = services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = databaseProvider,
                ConnectionString = database.ConnectionString(databaseProvider)
            }, migrations: [catalog]);
            registered.ThrowIfError();
        });
        var provider = host.Host.GetRequiredService<IRepositoryProvider>();
        // This isolated tutorial database starts empty. A deployment retains this history between releases.
        await database.ExecuteSetupAsync(provider,
            "DROP TABLE IF EXISTS migration_tutorial_tasks; DROP TABLE IF EXISTS migration_tutorial_outbox; DROP TABLE IF EXISTS nyssa_repository_migrations", cancellationToken);
        var runner = host.Host.GetRequiredService<IMigrationRunner>();

        var deployed = await runner.MigrateAsync("2.0.0-rc.1+linux.5", cancellationToken);
        deployed.ThrowIfError(); // Later release operations require successful initial deployment.

        var sameRelease = await runner.MigrateAsync("2.0.0-rc.1+linux.5", cancellationToken);
        // Build metadata is part of the pinned release. Preserve this expected rejection as a Result.
        var unpinnedBuild = await runner.MigrateAsync("2.0.0-rc.1+linux.6", cancellationToken);

        var previousRelease = await runner.MigrateAsync("1.0.0", cancellationToken);
        previousRelease.ThrowIfError(); // Reading that schema requires a successful rollback.

        await using var reading = await provider.OpenConnectionAsync(cancellationToken).UnwrapAsync();
        await using var history = reading.CreateCommand();
        history.CommandText = "SELECT COUNT(*) FROM nyssa_repository_migrations";
        var historyCount = Convert.ToInt64(await history.ExecuteScalarAsync(cancellationToken));

        await using var schema = reading.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM migration_tutorial_tasks";
        var taskCount = Convert.ToInt64(await schema.ExecuteScalarAsync(cancellationToken));

        var reset = await runner.MigrateAsync("0.0.0", cancellationToken);

        // Assert
        Assert.True(catalogResult.IsSuccess, catalogResult.ToString());
        Assert.True(registered.IsSuccess, registered.ToString());
        Assert.True(deployed.IsSuccess, deployed.ToString());
        Assert.Equal(0, deployed.Value.FromNumber);
        Assert.Equal(2, deployed.Value.ToNumber);
        Assert.Equal(2, deployed.Value.AppliedCount);
        Assert.True(sameRelease.IsSuccess, sameRelease.ToString());
        Assert.Equal(0, sameRelease.Value.AppliedCount);
        Assert.True(unpinnedBuild.IsFailure);
        Assert.True(previousRelease.IsSuccess, previousRelease.ToString());
        Assert.Equal(2, previousRelease.Value.FromNumber);
        Assert.Equal(1, previousRelease.Value.ToNumber);
        Assert.Equal(1, previousRelease.Value.AppliedCount);
        Assert.Equal(1L, historyCount);
        Assert.Equal(0L, taskCount);
        Assert.True(reset.IsSuccess, reset.ToString());
        Assert.Equal(0, reset.Value.ToNumber);
    }
}
