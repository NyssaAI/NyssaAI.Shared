using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Commands;
using NyssaAI.DbGateway.Configuration;
using Xunit;

namespace NyssaAI.DbGateway.Tests.Gateway;

public sealed class DbGatewayTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("nyssa-dbgateway-unit-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ExecuteAndQueryAsync_SqliteGateway_ExecutesTypedCommandsWithFullCoercion()
    {
        var dbPath = Path.Join(_directory, "tasks.db");
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        });

        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<IDbGateway>();
        Assert.Equal(DbDialect.Sqlite, gateway.Dialect);
        Assert.Equal("sqlite", gateway.DatabaseProvider);

        await gateway.ExecuteAsync(new CreateTasksTable(), TestContext.Current.CancellationToken);

        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var inserted = await gateway.ExecuteAsync(new InsertTask
        {
            Id = id,
            Title = "Verify coercion",
            IsCompleted = false,
            CreatedAt = createdAt
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, inserted);

        var fetched = await gateway.ExecuteAsync(new GetTaskById { Id = id }, TestContext.Current.CancellationToken);
        Assert.NotNull(fetched);
        Assert.Equal(id, fetched.Id);
        Assert.Equal("Verify coercion", fetched.Title);
        Assert.False(fetched.IsCompleted);
        Assert.Equal(createdAt, fetched.CreatedAt);

        var openTasks = await gateway.QueryAsync(new ListTasksByCompletion { IsCompleted = false }, TestContext.Current.CancellationToken);
        Assert.Single(openTasks);
        Assert.Equal(id, openTasks[0].Id);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_FailedStep_RollsBackEarlierWrites()
    {
        var dbPath = Path.Join(_directory, "tx-rollback.db");
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        });

        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<IDbGateway>();
        await gateway.ExecuteAsync(new CreateTasksTable(), TestContext.Current.CancellationToken);

        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await gateway.ExecuteTransactionAsync(async (tx, ct) =>
            {
                await tx.ExecuteAsync(new InsertTask
                {
                    Id = id,
                    Title = "Should rollback",
                    IsCompleted = false,
                    CreatedAt = DateTimeOffset.UtcNow
                }, ct);
                throw new InvalidOperationException("Simulated workflow failure.");
            }, TestContext.Current.CancellationToken);
        });

        var afterRollback = await gateway.ExecuteAsync(new GetTaskById { Id = id }, TestContext.Current.CancellationToken);
        Assert.Null(afterRollback);
    }

    [Fact]
    public async Task OpenTransactionAsync_FailedCommand_LatchesFailureAndPreventsCommit()
    {
        var dbPath = Path.Join(_directory, "tx-latch.db");
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = $"Data Source={dbPath}"
        });

        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<IDbGateway>();
        await gateway.ExecuteAsync(new CreateTasksTable(), TestContext.Current.CancellationToken);

        var id = Guid.NewGuid();
        await using (var tx = await gateway.OpenTransactionAsync(TestContext.Current.CancellationToken))
        {
            await tx.ExecuteAsync(new InsertTask
            {
                Id = id,
                Title = "First insert",
                IsCompleted = false,
                CreatedAt = DateTimeOffset.UtcNow
            }, TestContext.Current.CancellationToken);

            // Duplicate primary key fails on SQLite
            await Assert.ThrowsAnyAsync<Exception>(() => tx.ExecuteAsync(new InsertTask
            {
                Id = id,
                Title = "Duplicate insert",
                IsCompleted = true,
                CreatedAt = DateTimeOffset.UtcNow
            }, TestContext.Current.CancellationToken));

            var commitEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tx.CommitAsync(TestContext.Current.CancellationToken));
            Assert.NotNull(commitEx.InnerException);
        }

        var remaining = await gateway.ExecuteAsync(new GetTaskById { Id = id }, TestContext.Current.CancellationToken);
        Assert.Null(remaining);
    }

    private sealed record TaskRow
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required bool IsCompleted { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }

    private sealed record CreateTasksTable : IDbNonQuery
    {
        public string GetSql(DbDialect dialect) =>
            "CREATE TABLE tasks (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_completed INTEGER NOT NULL, created_at TEXT NOT NULL)";

        public void BindParameters(IParameterBinder parameters, DbDialect dialect)
        {
        }
    }

    private sealed record InsertTask : IDbNonQuery
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required bool IsCompleted { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }

        public string GetSql(DbDialect dialect) =>
            "INSERT INTO tasks (id, title, is_completed, created_at) VALUES (@id, @title, @isCompleted, @createdAt)";

        public void BindParameters(IParameterBinder parameters, DbDialect dialect)
        {
            parameters.Add("@id", Id);
            parameters.Add("@title", Title);
            parameters.Add("@isCompleted", IsCompleted);
            parameters.Add("@createdAt", CreatedAt);
        }
    }

    private sealed record GetTaskById : IDbCommand<TaskRow?>
    {
        public required Guid Id { get; init; }

        public string GetSql(DbDialect dialect) =>
            "SELECT id, title, is_completed, created_at FROM tasks WHERE id = @id";

        public void BindParameters(IParameterBinder parameters, DbDialect dialect) =>
            parameters.Add("@id", Id);

        public async ValueTask<TaskRow?> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
        {
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            return new TaskRow
            {
                Id = reader.GetGuid(0),
                Title = reader.GetString(1),
                IsCompleted = reader.GetBoolean(2),
                CreatedAt = reader.GetDateTimeOffset(3)
            };
        }
    }

    private sealed record ListTasksByCompletion : IDbQuery<TaskRow>
    {
        public required bool IsCompleted { get; init; }

        public string GetSql(DbDialect dialect) =>
            "SELECT id, title, is_completed, created_at FROM tasks WHERE is_completed = @isCompleted";

        public void BindParameters(IParameterBinder parameters, DbDialect dialect) =>
            parameters.Add("@isCompleted", IsCompleted);

        public TaskRow MapRow(IDataRecordReader reader) =>
            new()
            {
                Id = reader.GetGuid(0),
                Title = reader.GetString(1),
                IsCompleted = reader.GetBoolean(2),
                CreatedAt = reader.GetDateTimeOffset(3)
            };
    }
}
