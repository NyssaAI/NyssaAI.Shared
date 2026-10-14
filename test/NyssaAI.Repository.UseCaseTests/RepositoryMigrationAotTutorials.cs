using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
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
        using (var database = new DatabaseWorkspace())
        using (var host = new RepositoryHost())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // The application compiles both directions of each migration and pins releases to schema numbers.
            var catalog = MigrationCatalog.Create(
                databaseProvider,
                [
                    new()
                    {
                        Number = 1,
                        UpSql = "CREATE TABLE migration_tutorial_tasks(id BIGINT PRIMARY KEY, title TEXT NOT NULL)",
                        DownSql = "DROP TABLE migration_tutorial_tasks",
                    },
                    new()
                    {
                        Number = 2,
                        UpSql = "CREATE TABLE migration_tutorial_outbox(id BIGINT PRIMARY KEY, payload TEXT NOT NULL)",
                        DownSql = "DROP TABLE migration_tutorial_outbox",
                    },
                ],
                [
                    new() { Release = "0.0.0", Number = 0 },
                    new() { Release = "1.0.0", Number = 1 },
                    new() { Release = "2.0.0-rc.1+linux.5", Number = 2 },
                ]
            );
            host.Register(services =>
            {
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = databaseProvider,
                        ConnectionString = database.ConnectionString(databaseProvider),
                    },
                    migrations: [catalog]
                );
            });
            var provider = host.Host.GetRequiredService<IRepositoryProvider>();
            // This isolated tutorial database starts empty. A deployment retains this history between releases.
            await database.ResetMigrationsAsync(provider, cancellationToken);
            var runner = host.Host.GetRequiredService<IMigrationRunner>();
            var deployed = await runner.MigrateAsync("2.0.0-rc.1+linux.5", cancellationToken);
            var sameRelease = await runner.MigrateAsync("2.0.0-rc.1+linux.5", cancellationToken);
            // Build metadata is part of the pinned release; an unpinned build throws.
            var unpinnedBuild = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                runner.MigrateAsync("2.0.0-rc.1+linux.6", cancellationToken)
            );
            var previousRelease = await runner.MigrateAsync("1.0.0", cancellationToken);
            await using (var reading = await provider.OpenConnectionAsync(cancellationToken))
            await using (var history = reading.CreateCommand())
            {
                history.CommandText = "SELECT COUNT(*) FROM nyssa_repository_migrations";
                var historyCount = Convert.ToInt64(await history.ExecuteScalarAsync(cancellationToken));
                await using (var schema = reading.CreateCommand())
                {
                    schema.CommandText = "SELECT COUNT(*) FROM migration_tutorial_tasks";
                    var taskCount = Convert.ToInt64(await schema.ExecuteScalarAsync(cancellationToken));
                    var reset = await runner.MigrateAsync("0.0.0", cancellationToken);

                    // Assert
                    Assert.Equal(0, deployed.FromNumber);
                    Assert.Equal(2, deployed.ToNumber);
                    Assert.Equal(2, deployed.AppliedCount);
                    Assert.Equal(0, sameRelease.AppliedCount);

                    Assert.Equal(2, previousRelease.FromNumber);
                    Assert.Equal(1, previousRelease.ToNumber);
                    Assert.Equal(1, previousRelease.AppliedCount);
                    Assert.Equal(1L, historyCount);
                    Assert.Equal(0L, taskCount);
                    Assert.Equal(0, reset.ToNumber);
                }
            }
        }
    }
}
