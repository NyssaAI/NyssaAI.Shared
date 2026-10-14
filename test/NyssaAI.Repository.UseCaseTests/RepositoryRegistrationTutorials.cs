using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.UseCaseTests.domain;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryRegistrationTutorials
{
    private static readonly RepoCommandKey<int, int> Echo = new() { Name = "sample.echo" };
    private static readonly RepoCommandKey<int, int> Later = new() { Name = "sample.later" };
    private static RepositoryConfiguration Settings =>
        new() { DatabaseProvider = "sqlite", ConnectionString = "Data Source=:memory:" };

    [Fact]
    public async Task Register_InlineCommands_ExecutesCommandAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepository(
            Settings,
            commands: commands => commands.Add(Echo, value => new EchoCommand(Echo, value))
        );

        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            await using (var session = await repository.OpenSessionAsync(TestContext.Current.CancellationToken))
            {
                // Act
                var result = await session.ExecuteAsync(
                    repository.CommandRegistry(Echo, 42),
                    TestContext.Current.CancellationToken
                );

                // Assert
                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public async Task Register_SettingsFactory_ExecutesCommandAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepository(
            () => Settings,
            commands: commands => commands.Add(Echo, value => new EchoCommand(Echo, value))
        );

        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            await using (var session = await repository.OpenSessionAsync(TestContext.Current.CancellationToken))
            {
                // Act
                var result = await session.ExecuteAsync(
                    repository.CommandRegistry(Echo, 42),
                    TestContext.Current.CancellationToken
                );

                // Assert
                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public async Task Register_CommandsBeforeRepository_ExecutesCommandAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));
        services.AddNyssaRepository(Settings);

        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            await using (var session = await repository.OpenSessionAsync(TestContext.Current.CancellationToken))
            {
                // Act
                var result = await session.ExecuteAsync(
                    repository.CommandRegistry(Echo, 42),
                    TestContext.Current.CancellationToken
                );

                // Assert
                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public async Task Register_CommandsAfterRepository_ExecutesCommandAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepository(Settings);
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));

        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            await using (var session = await repository.OpenSessionAsync(TestContext.Current.CancellationToken))
            {
                // Act
                var result = await session.ExecuteAsync(
                    repository.CommandRegistry(Echo, 42),
                    TestContext.Current.CancellationToken
                );

                // Assert
                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public async Task Register_StoredSettings_ExecutesCommandAsync()
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var configurationHost = new ConfigurationHost())
        {
            var configuration = configurationHost.Open(database.DirectoryPath);
            await configuration.SetRepositoryConfigurationAsync(
                Settings,
                cancellationToken: TestContext.Current.CancellationToken
            );
            var services = new ServiceCollection();
            await services.AddNyssaRepositoryAsync(
                configuration,
                commands: commands => commands.Add(Echo, value => new EchoCommand(Echo, value)),
                cancellationToken: TestContext.Current.CancellationToken
            );

            await using (var container = services.BuildServiceProvider())
            {
                var repository = container.GetRequiredService<IRepository>();
                await using (var session = await repository.OpenSessionAsync(TestContext.Current.CancellationToken))
                {
                    // Act
                    var result = await session.ExecuteAsync(
                        repository.CommandRegistry(Echo, 42),
                        TestContext.Current.CancellationToken
                    );

                    // Assert
                    Assert.Equal(42, result);
                }
            }
        }
    }

    [Fact]
    public async Task CommandRegistry_RepeatedRequests_CreatesCommandsOnDemandAsync()
    {
        // Arrange
        var callbacks = 0;
        var factories = 0;
        var services = new ServiceCollection();
        services.AddNyssaRepository(
            Settings,
            commands: commands =>
            {
                callbacks++;
                commands.Add(
                    Echo,
                    value =>
                    {
                        factories++;
                        return new EchoCommand(Echo, value);
                    }
                );
            }
        );
        Assert.Equal(1, callbacks);
        Assert.Equal(0, factories);

        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();

            // Act
            var first = repository.CommandRegistry(Echo, 42);
            var second = repository.CommandRegistry(Echo, 43);

            // Assert
            Assert.NotSame(first, second);
            Assert.Equal(42, first.Parameters);
            Assert.Equal(43, second.Parameters);
            Assert.Equal(2, factories);
        }
    }

    [Fact]
    public async Task Register_MixedDefaultAndExplicitProviders_SelectsOnlyConfiguredFactoryAsync()
    {
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands =>
            commands
                .Add(Echo, value => new EchoCommand(Echo, value))
                .Add(" POSTGRESQL ", Echo, _ => throw new InvalidOperationException("Unselected factory"))
        );
        services.AddNyssaRepository(
            Settings,
            commands: commands => commands.Add(" SQLITE ", Later, value => new EchoCommand(Later, value))
        );
        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            Assert.Equal(7, repository.CommandRegistry(Echo, 7).Parameters);
            Assert.Equal(8, repository.CommandRegistry(Later, 8).Parameters);
        }
    }

    [Fact]
    public void Register_InlineDuplicate_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));

        var before = services.ToArray();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaRepository(
                Settings,
                commands: commands => commands.Add(Echo, value => new EchoCommand(Echo, value))
            )
        );

        // Assert

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_SeparateDuplicate_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));
        services.AddNyssaRepository(Settings);
        var before = services.ToArray();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
                commands.Add(" SQLITE ", Echo, value => new EchoCommand(Echo, value))
            )
        );

        // Assert

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_LegacyDuplicate_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));

        var before = services.ToArray();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaRepository(
                Settings,
                commands: [RepoCommandRegistration.Create("sqlite", Echo, value => new EchoCommand(Echo, value))]
            )
        );

        // Assert

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_DefaultProviderConflict_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));
        services.AddNyssaRepositoryCommands(commands =>
            commands.Add("sqlite", Echo, value => new EchoCommand(Echo, value))
        );
        var before = services.ToArray();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddNyssaRepository(Settings));

        // Assert

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_DifferentContractWithSameName_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNyssaRepositoryCommands(commands => commands.Add(Echo, value => new EchoCommand(Echo, value)));

        var before = services.ToArray();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
                commands.Add(
                    new RepoCommandKey<string, int> { Name = Echo.Name },
                    _ => throw new InvalidOperationException()
                )
            )
        );

        // Assert

        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_ThrowingCallback_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var before = services.ToArray();

        // Act
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
            {
                commands.Add(Echo, value => new EchoCommand(Echo, value));
                throw new InvalidOperationException("Application setup failed");
            })
        );

        // Assert
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_NullKey_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var before = services.ToArray();

        // Act
        Assert.Throws<ArgumentNullException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
            {
                commands.Add<int, int>(null!, value => new EchoCommand(Echo, value));
            })
        );

        // Assert
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_NullFactory_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var before = services.ToArray();

        // Act
        Assert.Throws<ArgumentNullException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
            {
                commands.Add(Echo, null!);
            })
        );

        // Assert
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Register_BlankProvider_LeavesServicesUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var before = services.ToArray();

        // Act
        Assert.Throws<ArgumentException>(() =>
            services.AddNyssaRepositoryCommands(commands =>
            {
                commands.Add(" ", Echo, value => new EchoCommand(Echo, value));
            })
        );

        // Assert
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public async Task Register_SnapshotsBuilderAndCollection_ExistingContainersKeepTheirCatalogAsync()
    {
        RepositoryCommandBuilder captured = null!;
        var services = new ServiceCollection();
        services.AddNyssaRepository(
            Settings,
            commands: commands =>
            {
                captured = commands;
                commands.Add(Echo, value => new EchoCommand(Echo, value));
            }
        );
        await using (var first = services.BuildServiceProvider())
        {
            captured.Add(Later, value => new EchoCommand(Later, value));
            services.AddNyssaRepositoryCommands(commands =>
                commands.Add(Later, value => new EchoCommand(Later, value))
            );
            await using (var second = services.BuildServiceProvider())
            {
                // Resolve after modifying the collection to prove the first container retained its snapshot.
                Assert.Throws<KeyNotFoundException>(() => first.GetRequiredService<IRepository>().CommandRegistry(Later, 1));
                Assert.Equal(2, second.GetRequiredService<IRepository>().CommandRegistry(Later, 2).Parameters);
            }
        }
    }

    [Fact]
    public async Task Resolve_ApplicationFactoryRejectsInput_PropagatesOriginalExceptionAsync()
    {
        var rejection = new ArgumentOutOfRangeException("value");
        var services = new ServiceCollection();
        services.AddNyssaRepository(Settings, commands: commands => commands.Add(Echo, _ => throw rejection));
        await using (var container = services.BuildServiceProvider())
        {
            var actual = Assert.Throws<ArgumentOutOfRangeException>(() =>
                container.GetRequiredService<IRepository>().CommandRegistry(Echo, -1)
            );
            Assert.Same(rejection, actual);
        }
    }
}
