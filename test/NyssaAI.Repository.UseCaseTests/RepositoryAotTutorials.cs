using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryAotTutorials
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task ExecuteAsync_ExternalCompiledCommand_ReturnsBufferedRowsAfterDisposalAsync(string databaseProvider)
    {
        // Arrange
        using var database = new DatabaseWorkspace();
        using var host = new RepositoryHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        host.Register(services => services.AddNyssaRepository(new RepositoryConfiguration
        {
            DatabaseProvider = databaseProvider,
            ConnectionString = database.ConnectionString(databaseProvider)
        }, commands: TaskOperations.Register(databaseProvider)).ThrowIfError());
        var provider = host.Host.GetRequiredService<IRepositoryProvider>();
        await database.ExecuteSetupAsync(provider,
            "DROP TABLE IF EXISTS f002_tasks; CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL)", cancellationToken);
        var repository = host.Host.GetRequiredService<IRepository>();
        var input = new TaskParameters { Id = 1, Title = "Write once, select either provider", Note = null };
        Result<int> written;
        Result committed;
        Result<IReadOnlyList<TaskRow>> listed;

        // Act
        // The external command catalog provides the SQL and result type for this named operation.
        // Unwrap throws on unexpected registration/resource failures before dependent execution.
        var resolved = repository.CommandRegistry(TaskOperations.Write, input);
        var write = resolved.Unwrap();
        var opened = await repository.OpenTransactionAsync(cancellationToken);
        await using (var transaction = opened.Unwrap())
        {
            written = await transaction.ExecuteAsync(write, cancellationToken);
            committed = await transaction.CommitAsync(cancellationToken);
        }

        var listing = repository.CommandRegistry(TaskOperations.List, input);
        var list = listing.Unwrap();
        var reading = await repository.OpenSessionAsync(cancellationToken);
        await using (var session = reading.Unwrap())
        {
            listed = await session.ExecuteAsync(list, cancellationToken);
        }
        // Results are buffered; their values can be read after the owning session is disposed.

        // Assert
        Assert.True(resolved.IsSuccess, resolved.ToString());
        Assert.True(opened.IsSuccess, opened.ToString());
        Assert.True(written.IsSuccess, written.ToString());
        Assert.Equal(1, written.Value);
        Assert.True(committed.IsSuccess, committed.ToString());
        Assert.True(listing.IsSuccess, listing.ToString());
        Assert.True(reading.IsSuccess, reading.ToString());
        Assert.True(listed.IsSuccess, listed.ToString());
        var row = Assert.Single(listed.Value);
        Assert.Equal("Write once, select either provider", row.Title);
        Assert.Null(row.Note);
    }
}
