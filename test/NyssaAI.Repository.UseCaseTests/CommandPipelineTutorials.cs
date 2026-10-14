using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

/// <summary>Typed dependent work with caller-owned transaction completion.</summary>
public sealed class CommandPipelineTutorials
{
    [Fact]
    public async Task ExecuteAsync_Imperative_PersistsTaskAndOutboxTogetherAsync()
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var host = new RepositoryHost())
        {
            host.Register(services =>
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = "sqlite",
                        ConnectionString = database.ConnectionString("sqlite"),
                    },
                    commands: TaskOperations.Register("sqlite")
                )
            );
            var repository = host.Host.GetRequiredService<IRepository>();
            var provider = host.Host.GetRequiredService<IRepositoryProvider>();
            var cancellationToken = TestContext.Current.CancellationToken;
            await database.PrepareTasksAsync(provider, cancellationToken);
            var parameters = new TaskParameters
            {
                Id = 7,
                Title = "Ship library",
                Note = null,
            };
            int saved;

            // Act
            // Opening returns the transaction directly or throws.
            await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
            {
                // The same workflow can execute each typed command explicitly.
                await transaction.ExecuteAsync(
                    repository.CommandRegistry(TaskOperations.Write, parameters),
                    cancellationToken
                );
                var task = await transaction.ExecuteAsync(
                    repository.CommandRegistry(TaskOperations.Find, parameters),
                    cancellationToken
                );
                saved = await transaction.ExecuteAsync(
                    repository.CommandRegistry(
                        TaskOperations.Outbox,
                        new TaskParameters
                        {
                            Id = task.Id,
                            Title = task.Title,
                            Note = task.Note,
                        }
                    ),
                    cancellationToken
                );
                // Completion belongs to the caller; a failed command prevents a successful commit.
                await transaction.CommitAsync(cancellationToken);
            }

            // An external publisher sees committed outbox entries only after transaction disposal.
            await using (var reading = await provider.OpenConnectionAsync(cancellationToken))
            await using (var records = reading.CreateCommand())
            {
                records.CommandText =
                    "SELECT COUNT(*) FROM f002_tasks AS tasks JOIN f002_outbox AS outbox ON tasks.id = outbox.task_id AND tasks.title = outbox.title";
                var pairedRecords = Convert.ToInt64(await records.ExecuteScalarAsync(cancellationToken));

                // Assert
                Assert.Equal(1, saved);
                Assert.Equal(1L, pairedRecords);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Fluent_PersistsTaskAndOutboxTogetherAsync()
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var host = new RepositoryHost())
        {
            host.Register(services =>
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = "sqlite",
                        ConnectionString = database.ConnectionString("sqlite"),
                    },
                    commands: TaskOperations.Register("sqlite")
                )
            );
            var repository = host.Host.GetRequiredService<IRepository>();
            var provider = host.Host.GetRequiredService<IRepositoryProvider>();
            var cancellationToken = TestContext.Current.CancellationToken;
            await database.PrepareTasksAsync(provider, cancellationToken);
            var parameters = new TaskParameters
            {
                Id = 7,
                Title = "Ship library",
                Note = null,
            };
            int saved;

            // Act
            // Opening returns the transaction directly or throws.
            await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
            {
                // Then passes the previous typed result to the next command, in this transaction.
                saved = await transaction
                    .Pipeline(repository, TaskOperations.Write, parameters)
                    .Then(TaskOperations.Find, _ => parameters)
                    .Then(
                        TaskOperations.Outbox,
                        task => new TaskParameters
                        {
                            Id = task.Id,
                            Title = task.Title,
                            Note = task.Note,
                        }
                    )
                    .ExecuteAsync(cancellationToken);
                // Completion belongs to the caller; a failed command prevents a successful commit.
                await transaction.CommitAsync(cancellationToken);
            }

            // An external publisher sees committed outbox entries only after transaction disposal.
            await using (var reading = await provider.OpenConnectionAsync(cancellationToken))
            await using (var records = reading.CreateCommand())
            {
                records.CommandText =
                    "SELECT COUNT(*) FROM f002_tasks AS tasks JOIN f002_outbox AS outbox ON tasks.id = outbox.task_id AND tasks.title = outbox.title";
                var pairedRecords = Convert.ToInt64(await records.ExecuteScalarAsync(cancellationToken));

                // Assert
                Assert.Equal(1, saved);
                Assert.Equal(1L, pairedRecords);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_NotificationValidationFails_RollsBackTaskAsync()
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var host = new RepositoryHost())
        {
            host.Register(services =>
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = "sqlite",
                        ConnectionString = database.ConnectionString("sqlite"),
                    },
                    commands: TaskOperations.Register("sqlite")
                )
            );
            var repository = host.Host.GetRequiredService<IRepository>();
            var provider = host.Host.GetRequiredService<IRepositoryProvider>();
            var cancellationToken = TestContext.Current.CancellationToken;
            await database.PrepareTasksAsync(provider, cancellationToken);
            InvalidOperationException saved;

            // Act
            // Opening throws if a transaction cannot be obtained.
            await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
            {
                saved = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    transaction
                        .Pipeline(
                            repository,
                            TaskOperations.Write,
                            new TaskParameters
                            {
                                Id = 7,
                                Title = "Ship library",
                                Note = null,
                            }
                        )
                        .Then(
                            TaskOperations.Outbox,
                            _ => throw new InvalidOperationException("The notification recipient is unavailable.")
                        )
                        .ExecuteAsync(cancellationToken)
                );
                // Catching the exception does not make the earlier write committable.
                await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(cancellationToken));
                await transaction.RollbackAsync(cancellationToken);
            }

            await using (var reading = await provider.OpenConnectionAsync(cancellationToken))
            await using (var taskCount = reading.CreateCommand())
            {
                taskCount.CommandText = "SELECT COUNT(*) FROM f002_tasks";
                var tasks = Convert.ToInt64(await taskCount.ExecuteScalarAsync(cancellationToken));
                await using (var outboxCount = reading.CreateCommand())
                {
                    outboxCount.CommandText = "SELECT COUNT(*) FROM f002_outbox";
                    var notifications = Convert.ToInt64(await outboxCount.ExecuteScalarAsync(cancellationToken));

                    // Assert
                    Assert.Equal("The notification recipient is unavailable.", saved.Message);
                    Assert.Equal(0L, tasks);
                    Assert.Equal(0L, notifications);
                }
            }
        }
    }
}
