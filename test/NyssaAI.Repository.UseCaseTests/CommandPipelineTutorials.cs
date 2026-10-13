using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

/// <summary>Typed dependent work with caller-owned transaction completion.</summary>
public sealed class CommandPipelineTutorials
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_FluentOrImperative_PersistsTaskAndOutboxTogetherAsync(bool fluent)
    {
        // Arrange
        using var database = new DatabaseWorkspace();
        using var host = new RepositoryHost();
        host.Register(services => services.AddNyssaRepository(new RepositoryConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = database.ConnectionString("sqlite")
        }, commands: TaskOperations.Register("sqlite")).ThrowIfError());
        var repository = host.Host.GetRequiredService<IRepository>();
        var provider = host.Host.GetRequiredService<IRepositoryProvider>();
        var cancellationToken = TestContext.Current.CancellationToken;
        await database.ExecuteSetupAsync(provider,
            "CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL); CREATE TABLE f002_outbox (task_id BIGINT PRIMARY KEY, title TEXT NOT NULL)", cancellationToken);
        var parameters = new TaskParameters { Id = 7, Title = "Ship library", Note = null };
        Result<int> saved;
        Result committed;

        // Act
        // Unwrap throws if no transaction can be opened: subsequent writes require that resource.
        await using (var transaction = await repository.OpenTransactionAsync(cancellationToken).UnwrapAsync())
        {
            if (fluent)
            {
                // Then passes the previous typed result to the next command, in this transaction.
                saved = await transaction.Pipeline(repository, TaskOperations.Write, parameters)
                    .Then(TaskOperations.Find, _ => parameters)
                    .Then(TaskOperations.Outbox, task => new TaskParameters { Id = task.Id, Title = task.Title, Note = task.Note })
                    .ExecuteAsync(cancellationToken);
            }
            else
            {
                // The same workflow can execute each typed command explicitly.
                var write = repository.CommandRegistry(TaskOperations.Write, parameters).Unwrap();
                var written = await transaction.ExecuteAsync(write, cancellationToken);
                var query = repository.CommandRegistry(TaskOperations.Find, parameters).Unwrap();
                var found = await written.ThenAsync(_ => transaction.ExecuteAsync(query, cancellationToken));
                saved = await found.ThenAsync(task =>
                {
                    var notification = repository.CommandRegistry(TaskOperations.Outbox,
                        new TaskParameters { Id = task.Id, Title = task.Title, Note = task.Note });
                    return notification.ThenAsync(command => transaction.ExecuteAsync(command, cancellationToken));
                });
            }

            // Completion belongs to the caller; a failed command prevents a successful commit.
            committed = await transaction.CommitAsync(cancellationToken);
        }

        // An external publisher sees committed outbox entries only after transaction disposal.
        await using var reading = await provider.OpenConnectionAsync(cancellationToken).UnwrapAsync();
        await using var records = reading.CreateCommand();
        records.CommandText = "SELECT COUNT(*) FROM f002_tasks AS tasks JOIN f002_outbox AS outbox ON tasks.id = outbox.task_id AND tasks.title = outbox.title";
        var pairedRecords = Convert.ToInt64(await records.ExecuteScalarAsync(cancellationToken));

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.Equal(1, saved.Value);
        Assert.True(committed.IsSuccess, committed.ToString());
        Assert.Equal(1L, pairedRecords);
    }

    [Fact]
    public async Task ExecuteAsync_NotificationValidationFails_RollsBackTaskAsync()
    {
        // Arrange
        using var database = new DatabaseWorkspace();
        using var host = new RepositoryHost();
        host.Register(services => services.AddNyssaRepository(new RepositoryConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = database.ConnectionString("sqlite")
        }, commands: TaskOperations.Register("sqlite")).ThrowIfError());
        var repository = host.Host.GetRequiredService<IRepository>();
        var provider = host.Host.GetRequiredService<IRepositoryProvider>();
        var cancellationToken = TestContext.Current.CancellationToken;
        await database.ExecuteSetupAsync(provider,
            "CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL); CREATE TABLE f002_outbox (task_id BIGINT PRIMARY KEY, title TEXT NOT NULL)", cancellationToken);
        Result<int> saved;
        Result committed;
        Result rolledBack;

        // Act
        // Opening is a prerequisite; UnwrapAsync throws if a transaction cannot be obtained.
        await using (var transaction = await repository.OpenTransactionAsync(cancellationToken).UnwrapAsync())
        {
            saved = await transaction.Pipeline(repository, TaskOperations.Write,
                    new TaskParameters { Id = 7, Title = "Ship library", Note = null })
                .Then(TaskOperations.Outbox, _ => Result<TaskParameters>.Failure(
                    Error.FromMessage("The notification recipient is unavailable.", ErrorCategory.Validation)))
                .ExecuteAsync(cancellationToken);

            // Keep expected failures as Results: failed dependent work blocks committing the earlier write.
            committed = await transaction.CommitAsync(cancellationToken);
            rolledBack = await transaction.RollbackAsync(cancellationToken);
        }

        await using var reading = await provider.OpenConnectionAsync(cancellationToken).UnwrapAsync();
        await using var taskCount = reading.CreateCommand();
        taskCount.CommandText = "SELECT COUNT(*) FROM f002_tasks";
        var tasks = Convert.ToInt64(await taskCount.ExecuteScalarAsync(cancellationToken));

        await using var outboxCount = reading.CreateCommand();
        outboxCount.CommandText = "SELECT COUNT(*) FROM f002_outbox";
        var notifications = Convert.ToInt64(await outboxCount.ExecuteScalarAsync(cancellationToken));

        // Assert
        Assert.True(saved.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(saved.Errors).Category);
        Assert.True(committed.IsFailure);
        Assert.True(rolledBack.IsSuccess, rolledBack.ToString());
        Assert.Equal(0L, tasks);
        Assert.Equal(0L, notifications);
    }
}
