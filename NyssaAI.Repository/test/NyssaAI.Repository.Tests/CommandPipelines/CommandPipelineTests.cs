using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.CommandPipelines;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NyssaAI.Repository.Tests.CommandPipelines;

public sealed class CommandPipelineTests
{
    private static readonly RepoCommandKey<TaskChange, TaskChanged> ChangeKey = new() { Name = "task.change" };
    private static readonly RepoCommandKey<Notification, int> NotifyKey = new() { Name = "outbox.insert" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_TypedDependentSteps_UseCallerScopeAndExplicitCommit(bool fluent)
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            var opened = await fixture.Repository.OpenTransactionAsync(TestContext.Current.CancellationToken);

            await using (var transaction = opened)
            {
                int result;
                if (fluent)
                {
                    var pipeline = transaction.Pipeline(ChangeKey, new TaskChange { Id = 41 })
                        .Then(NotifyKey, changed => new Notification { TaskId = changed.Id });
                    Assert.Equal(0, fixture.CommandCalls);
                    result = await pipeline.ExecuteAsync(TestContext.Current.CancellationToken);
                }
                else
                {
                    var changed = await transaction.ExecuteAsync(ChangeKey, new TaskChange { Id = 41 }, TestContext.Current.CancellationToken);

                    result = await transaction.ExecuteAsync(NotifyKey, new Notification { TaskId = changed.Id }, TestContext.Current.CancellationToken);
                }

                Assert.Equal(1, result);
                Assert.Equal(2, fixture.CommandCalls);
                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            }
            Assert.Equal(1, await fixture.CountAsync("tasks"));
            Assert.Equal(1, await fixture.CountAsync("outbox"));
            Assert.Equal(41L, await fixture.ScalarAsync("SELECT task_id FROM outbox"));
        }
    }

    [Theory]
    [InlineData("missing", typeof(KeyNotFoundException))]
    [InlineData("parameters", typeof(ArgumentException))]
    [InlineData("factory-null", typeof(ArgumentNullException))]
    [InlineData("parameters-null", typeof(ArgumentNullException))]
    [InlineData("canceled", typeof(OperationCanceledException))]
    [InlineData("cancellation-exception", typeof(OperationCanceledException))]
    [InlineData("database", typeof(SqliteException))]
    public async Task ExecuteAsync_LaterFailure_StopsWorkAndPreventsCommit(string failure, Type exceptionType)
    {
        await using (var fixture = await PipelineDatabase.CreateAsync(failure == "database"))
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var opened = await fixture.Repository.OpenTransactionAsync(TestContext.Current.CancellationToken);

                var downstreamCalls = 0;
                await using (var transaction = opened)
                {
                    var key = failure == "missing" ? new RepoCommandKey<Notification, int> { Name = "missing" } : NotifyKey;
                    var pipeline = transaction.Pipeline(fixture.Repository, ChangeKey, new TaskChange { Id = 42 })
                        .Then(key, changed =>
                        {
                            if (failure == "canceled") cancellation.Cancel();
                            if (failure == "cancellation-exception") throw new OperationCanceledException();
                            if (failure == "factory-null") return null!;
                            if (failure == "parameters-null") return null!;
                            return failure == "parameters"
                                ? throw new ArgumentException("No notification is valid.")
                                : new Notification { TaskId = changed.Id };
                        })
                        .Then(NotifyKey, _ => { downstreamCalls++; return new Notification { TaskId = 99 }; });
                    var failureException = await Assert.ThrowsAsync(exceptionType, () => pipeline.ExecuteAsync(cancellation.Token));
                    Assert.Equal(0, downstreamCalls);
                    Assert.Equal(failure == "database" ? 2 : 1, fixture.CommandCalls);
                    Assert.Equal(failure == "database" ? 2 : 1, fixture.FactoryCalls);
                    var rejection = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                    Assert.Same(failureException, rejection.InnerException);
                    await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                }
                Assert.Equal(0, await fixture.CountAsync("tasks"));
                Assert.Equal(0, await fixture.CountAsync("outbox"));
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ParameterFactoryThrows_LeavesTransactionUncommittable()
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            var opened = await fixture.Repository.OpenTransactionAsync(TestContext.Current.CancellationToken);

            await using (var transaction = opened)
            {
                var pipeline = transaction.Pipeline(fixture.Repository, ChangeKey, new TaskChange { Id = 43 })
                    .Then(NotifyKey, (Func<TaskChanged, Notification>)(_ => throw new InvalidOperationException("author defect")));
                await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
            }
            Assert.Equal(0, await fixture.CountAsync("tasks"));
        }
    }

    [Fact]
    public async Task ExecuteAsync_FirstCommandFails_DoesNotInvokeDependentFactory()
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            await using (var session = (await fixture.Repository.OpenSessionAsync(TestContext.Current.CancellationToken)))
            {
                var calls = 0;
                var result = await Assert.ThrowsAsync<KeyNotFoundException>(() => session.Pipeline(fixture.Repository, new RepoCommandKey<TaskChange, TaskChanged> { Name = "missing" }, new TaskChange { Id = 44 })
                    .Then(NotifyKey, _ => { calls++; return new Notification { TaskId = 44 }; })
                    .ExecuteAsync(TestContext.Current.CancellationToken));

                Assert.Equal(0, calls);
                Assert.Equal(0, fixture.CommandCalls);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_SessionFailure_DoesNotImplyTransactionAtomicity()
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            await using (var session = (await fixture.Repository.OpenSessionAsync(TestContext.Current.CancellationToken)))
            {
                var result = await Assert.ThrowsAsync<KeyNotFoundException>(() => session.Pipeline(fixture.Repository, ChangeKey, new TaskChange { Id = 44 })
                    .Then(new RepoCommandKey<Notification, int> { Name = "missing" }, changed => new Notification { TaskId = changed.Id })
                    .ExecuteAsync(TestContext.Current.CancellationToken));

                Assert.Equal(1, await fixture.CountAsync("tasks"));
                Assert.Equal(0, await fixture.CountAsync("outbox"));
                await session.Pipeline(fixture.Repository, NotifyKey, new Notification { TaskId = 44 })
                    .ExecuteAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    [Theory]
    [InlineData("disposed", typeof(ObjectDisposedException))]
    [InlineData("committed", typeof(InvalidOperationException))]
    [InlineData("failed", typeof(InvalidOperationException))]
    [InlineData("canceled", typeof(OperationCanceledException))]
    public async Task ExecuteAsync_ScopeUnavailable_ChecksLifecycleBeforeResolution(string state, Type exceptionType)
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            await using (var transaction = (await fixture.Repository.OpenTransactionAsync(TestContext.Current.CancellationToken)))
            {
                if (state == "disposed") await transaction.DisposeAsync();
                if (state == "committed") await transaction.CommitAsync(TestContext.Current.CancellationToken);
                if (state == "failed") await Assert.ThrowsAsync<KeyNotFoundException>(() => transaction.Pipeline(fixture.Repository,
                    new RepoCommandKey<TaskChange, TaskChanged> { Name = "missing" }, new TaskChange { Id = 1 })
                    .ExecuteAsync(TestContext.Current.CancellationToken));
                using (var cancellation = new CancellationTokenSource())
                {
                    if (state == "canceled") cancellation.Cancel();
                    await Assert.ThrowsAsync(exceptionType, () => transaction.Pipeline(fixture.Repository, ChangeKey, new TaskChange { Id = 1 }).ExecuteAsync(cancellation.Token));
                    Assert.Equal(0, fixture.FactoryCalls);
                    Assert.Equal(0, fixture.CommandCalls);
                }
            }
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task ExecuteAsync_RepeatedExecution_RerunsWithinCallerOwnedScope(bool sessionOnly, int expected)
    {
        await using (var fixture = await PipelineDatabase.CreateAsync())
        {
            var scope = sessionOnly ? (await fixture.Repository.OpenSessionAsync(TestContext.Current.CancellationToken))
                : (await fixture.Repository.OpenTransactionAsync(TestContext.Current.CancellationToken));
            await using (scope)
            {
                var pipeline = scope.Pipeline(fixture.Repository, ChangeKey, new TaskChange { Id = 45 })
                    .Then(NotifyKey, changed => new Notification { TaskId = changed.Id });
                await pipeline.ExecuteAsync(TestContext.Current.CancellationToken);
                await pipeline.ExecuteAsync(TestContext.Current.CancellationToken);
            }
            Assert.Equal(expected, await fixture.CountAsync("tasks"));
            Assert.Equal(expected, await fixture.CountAsync("outbox"));
        }
    }

    private sealed record TaskChange
    {
        public required int Id { get; init; }
    }

    private sealed record TaskChanged
    {
        public required int Id { get; init; }
    }

    private sealed record Notification
    {
        public required int TaskId { get; init; }
    }

    private sealed class PipelineDatabase : IAsyncDisposable
    {
        private readonly ServiceProvider _container;
        private readonly DbConnection _anchor;
        private int _factoryCalls;
        private int _commandCalls;

        private PipelineDatabase(ServiceProvider container, DbConnection anchor)
        {
            _container = container;
            _anchor = anchor;
        }

        internal IRepository Repository => _container.GetRequiredService<IRepository>();
        internal int FactoryCalls => _factoryCalls;
        internal int CommandCalls => _commandCalls;

        internal static async Task<PipelineDatabase> CreateAsync(bool failNotification = false)
        {
            PipelineDatabase fixture = null!;
            var change = RepoCommandRegistration.Create("sqlite", ChangeKey, parameters =>
            {
                fixture._factoryCalls++;
                return new PipelineCommand<TaskChange, TaskChanged>(ChangeKey, parameters,
                    async (connection, transaction, token) =>
                    {
                        fixture._commandCalls++;
                        await InsertAsync(connection, transaction, "INSERT INTO tasks VALUES ($id)", parameters.Id, token);
                        return new TaskChanged { Id = parameters.Id };
                    });
            });
            var notify = RepoCommandRegistration.Create("sqlite", NotifyKey, parameters =>
            {
                fixture._factoryCalls++;
                return new PipelineCommand<Notification, int>(NotifyKey, parameters,
                    async (connection, transaction, token) =>
                    {
                        fixture._commandCalls++;
                        return await InsertAsync(connection, transaction,
                            failNotification ? "INSERT INTO missing VALUES ($id)" : "INSERT INTO outbox VALUES ($id)", parameters.TaskId, token);
                    });
            });

            var services = new ServiceCollection();
            services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = "sqlite", ConnectionString = $"Data Source=pipeline-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
            }, commands: [change, notify]);
            var container = services.BuildServiceProvider();
            var opened = await container.GetRequiredService<IRepositoryProvider>().OpenConnectionAsync(TestContext.Current.CancellationToken);

            fixture = new PipelineDatabase(container, opened);
            await using (var setup = opened.CreateCommand())
            {
                setup.CommandText = "CREATE TABLE tasks (id INTEGER); CREATE TABLE outbox (task_id INTEGER)";
                await setup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                return fixture;
            }
        }

        internal async Task<int> CountAsync(string table) => Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}"));

        internal async Task<long> ScalarAsync(string sql)
        {
            await using (var command = _anchor.CreateCommand())
            {
                command.CommandText = sql;
                return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _anchor.DisposeAsync();
            await _container.DisposeAsync();
        }

        private static async Task<int> InsertAsync(DbConnection connection, DbTransaction? transaction, string sql, int id, CancellationToken token)
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$id";
                parameter.Value = id;
                command.Parameters.Add(parameter);
                return await command.ExecuteNonQueryAsync(token);
            }
        }
    }

    private sealed class PipelineCommand<TParameters, TResult> : IRepoCommand<TParameters, TResult>
    {
        private readonly Func<DbConnection, DbTransaction?, CancellationToken, Task<TResult>> _execute;

        internal PipelineCommand(RepoCommandKey<TParameters, TResult> key, TParameters parameters,
            Func<DbConnection, DbTransaction?, CancellationToken, Task<TResult>> execute)
        {
            Key = key;
            Parameters = parameters;
            _execute = execute;
        }

        public RepoCommandKey<TParameters, TResult> Key { get; }
        public TParameters Parameters { get; }
        public Task<TResult> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken) =>
            _execute(connection, transaction, cancellationToken);
    }
}
