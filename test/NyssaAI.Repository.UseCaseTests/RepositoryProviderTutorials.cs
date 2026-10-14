using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.domain;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryProviderTutorials
{
    [Fact]
    public async Task Configure_SwitchDatabase_ExistingProviderKeepsSnapshotAsync()
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var configurationHost = new ConfigurationHost())
        {
            var store = configurationHost.Open(database.DirectoryPath);
            using (var existingHost = new RepositoryHost())
            using (var replacementHost = new RepositoryHost())
            {
                var postgres = database.ConnectionString("postgresql");
                var cancellationToken = TestContext.Current.CancellationToken;

                // Act
                // The first save defaults to plaintext. A later host loads these settings.
                await store.SetRepositoryConfigurationAsync(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = "sqlite",
                        ConnectionString = "Data Source=:memory:",
                    },
                    cancellationToken: cancellationToken
                );
                await existingHost.RegisterAsync(async services =>
                {
                    await services.AddNyssaRepositoryAsync(store, cancellationToken: cancellationToken);
                });
                var existing = existingHost.Host.GetRequiredService<IRepositoryProvider>();
                // Saving changes affects future registrations, not an existing provider's captured settings.
                await store.SetRepositoryConfigurationAsync(
                    new RepositoryConfiguration { DatabaseProvider = "postgresql", ConnectionString = postgres },
                    cancellationToken: cancellationToken
                );
                await replacementHost.RegisterAsync(async services =>
                {
                    await services.AddNyssaRepositoryAsync(store, cancellationToken: cancellationToken);
                });
                var replacement = replacementHost.Host.GetRequiredService<IRepositoryProvider>();
                // Both captured providers remain usable. The caller owns each returned connection.
                await using (var existingConnection = await existing.OpenConnectionAsync(cancellationToken))
                await using (var existingQuery = existingConnection.CreateCommand())
                {
                    existingQuery.CommandText = "SELECT 1";
                    var existingValue = Convert.ToInt64(await existingQuery.ExecuteScalarAsync(cancellationToken));
                    await using (var replacementConnection = await replacement.OpenConnectionAsync(cancellationToken))
                    await using (var replacementQuery = replacementConnection.CreateCommand())
                    {
                        replacementQuery.CommandText = "SELECT 1";
                        var replacementValue = Convert.ToInt64(
                            await replacementQuery.ExecuteScalarAsync(cancellationToken)
                        );

                        // Assert
                        Assert.Equal("sqlite", existing.DatabaseProvider);
                        Assert.Equal("postgresql", replacement.DatabaseProvider);
                        Assert.Equal(1L, existingValue);
                        Assert.Equal(1L, replacementValue);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Configure_CompiledCustomProvider_UsesPublicRegistrationSeamAsync()
    {
        // Arrange
        using (var host = new RepositoryHost())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            // A provider contributes connections through the public seam; repository workflows stay Nyssa's.
            var echo = new RepoCommandKey<int, int> { Name = "sample.echo" };
            var custom = new RepositoryProviderRegistration
            {
                DatabaseProvider = "application-sqlite",
                Prepare = settings => () => new ApplicationProvider(settings),
            };
            host.Register(services =>
            {
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = "APPLICATION-SQLITE",
                        ConnectionString = "Data Source=:memory:",
                    },
                    providers: [custom],
                    commands: commands => commands.Add(echo, value => new EchoCommand(echo, value))
                );
            });
            var repository = host.Host.GetRequiredService<IRepository>();
            await using (var session = await repository.OpenSessionAsync(cancellationToken))
            {
                var value = await session.ExecuteAsync(repository.CommandRegistry(echo, 42), cancellationToken);

                // Assert
                Assert.Equal(42, value);
            }
        }
    }
    [Fact]
    public async Task OpenConnectionAsync_CanceledWhileWaitingForPool_PreservesCallerTokenAndPoolCapacity()
    {
        using (var database = new DatabaseWorkspace())
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString("postgresql"))
            {
                Pooling = true,
                MaxPoolSize = 1,
            };
            var services = new ServiceCollection();
            services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = "postgresql",
                ConnectionString = connection.ConnectionString,
            });
            await using (var container = services.BuildServiceProvider())
            {
                var provider = container.GetRequiredService<IRepositoryProvider>();
                await using (var occupied = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken))
                using (var cancellation = new CancellationTokenSource())
                {
                    var waiting = provider.OpenConnectionAsync(cancellation.Token);
                    Assert.False(waiting.IsCompleted);
                    await cancellation.CancelAsync();
                    var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
                    Assert.Equal(cancellation.Token, exception.CancellationToken);
                }
                await using (var available = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken))
                {
                    Assert.Equal(System.Data.ConnectionState.Open, available.State);
                }
            }
        }
    }
}
