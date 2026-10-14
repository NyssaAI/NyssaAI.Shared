using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.AotCommands.Tasks;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryAotTutorials
{
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task ExecuteAsync_ExternalCompiledCommand_ReturnsBufferedRowsAfterDisposalAsync(
        string databaseProvider
    )
    {
        // Arrange
        using (var database = new DatabaseWorkspace())
        using (var host = new RepositoryHost())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            host.Register(services =>
                services.AddNyssaRepository(
                    new RepositoryConfiguration
                    {
                        DatabaseProvider = databaseProvider,
                        ConnectionString = database.ConnectionString(databaseProvider),
                    },
                    commands: TaskOperations.Register(databaseProvider)
                )
            );
            var provider = host.Host.GetRequiredService<IRepositoryProvider>();
            await database.PrepareTasksAsync(provider, cancellationToken);
            var repository = host.Host.GetRequiredService<IRepository>();
            var input = new TaskParameters
            {
                Id = 1,
                Title = "Write once, select either provider",
                Note = null,
            };
            int written;
            IReadOnlyList<TaskRow> listed;

            // Act
            // The external command catalog provides the SQL and result type for this named operation.
            // Selection and opening throw before dependent execution when they fail.
            var write = repository.CommandRegistry(TaskOperations.Write, input);
            await using (var transaction = await repository.OpenTransactionAsync(cancellationToken))
            {
                written = await transaction.ExecuteAsync(write, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            var list = repository.CommandRegistry(TaskOperations.List, input);
            await using (var session = await repository.OpenSessionAsync(cancellationToken))
            {
                listed = await session.ExecuteAsync(list, cancellationToken);
            }

            // Results are buffered; their values can be read after the owning session is disposed.

            // Assert
            Assert.Equal(1, written);
            var row = Assert.Single(listed);
            Assert.Equal("Write once, select either provider", row.Title);
            Assert.Null(row.Note);
        }
    }
}
