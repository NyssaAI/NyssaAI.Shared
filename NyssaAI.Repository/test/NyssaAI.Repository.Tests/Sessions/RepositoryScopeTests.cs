using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NyssaAI.Repository.Tests.Sessions;

public sealed class RepositoryScopeTests
{
    private static readonly RepoCommandKey<CommandParameters, int> Key = new() { Name = "execute" };

    [Theory]
    [InlineData("provider")]
    [InlineData("key")]
    [InlineData("factory")]
    public void Create_MissingRegistrationInput_ReturnsValidation(string missing)
    {
        var result = Assert.ThrowsAny<ArgumentException>(() => RepoCommandRegistration.Create(missing == "provider" ? " " : "sqlite",
            missing == "key" ? null! : Key, missing == "factory" ? null! : Command));

    }

    [Theory]
    [InlineData("key")]
    [InlineData("parameters")]
    [InlineData("factory-command")]
    public async Task CommandRegistry_NullAtRuntime_ReturnsValidation(string missing)
    {
        await using (var container = Build([Register(parameters => missing switch
        {
            "factory-command" => null!,
            _ => Command(parameters)
        })]))
        {
            var repository = container.GetRequiredService<IRepository>();
            var result = Assert.Throws(missing == "factory-command" ? typeof(InvalidOperationException) : typeof(ArgumentNullException), () => repository.CommandRegistry(missing == "key" ? null! : Key,
                missing == "parameters" ? null! : new CommandParameters { Sql = "unused" }));

        }
    }

    [Fact]
    public async Task ExecuteAsync_CommandFromAnotherRepositoryWithSameProvider_IsCompatible()
    {
        await using (var first = Build([Register(Command)]))
        {
            await using (var second = Build([]))
            {
                var command = first.GetRequiredService<IRepository>().CommandRegistry(Key, new CommandParameters { Sql = "SELECT 1" });
                await using (var session = (await second.GetRequiredService<IRepository>().OpenSessionAsync(TestContext.Current.CancellationToken)))
                {
                    await session.ExecuteAsync(command, TestContext.Current.CancellationToken);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandRegistry_TypedFactory_IsFreshAndDoesNotOpenDatabase(bool callback)
    {
        var calls = 0;
        var registration = Register(parameters => { calls++; return Command(parameters); });
        var services = new ServiceCollection();
        var settings = new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = "Data Source=:memory:" };
        if (callback) services.AddNyssaRepository(() => settings, commands: [registration]);
        else services.AddNyssaRepository(settings, commands: [registration]);
        await using (var container = services.BuildServiceProvider())
        {
            var repository = container.GetRequiredService<IRepository>();
            var parameters = new CommandParameters { Sql = "SELECT 1" };
            Assert.Equal(0, calls);
            var first = repository.CommandRegistry(Key, parameters);
            var second = repository.CommandRegistry(Key, parameters);

            Assert.Equal(2, calls);
            Assert.NotSame(first, second);
            Assert.Same(parameters, first.Parameters);
        }
    }

    [Theory]
    [InlineData("missing", typeof(KeyNotFoundException))]
    [InlineData("incompatible", typeof(InvalidOperationException))]
    [InlineData("parameters", typeof(InvalidOperationException))]
    [InlineData("key", typeof(InvalidOperationException))]
    [InlineData("validation", typeof(InvalidOperationException))]
    public async Task CommandRegistry_InvalidRegistration_ReturnsFailure(string scenario, Type exceptionType)
    {
        var registration = Register(parameters => scenario switch
        {
            "parameters" => Command(parameters with { Sql = "SELECT 2" }),
            "key" => new SqlCommand(parameters, new() { Name = "other" }),
            "validation" => throw new InvalidOperationException("Rejected parameters"),
            _ => Command(parameters)
        });
        var incompatible = RepoCommandRegistration.Create("sqlite", new RepoCommandKey<string, int> { Name = Key.Name },
            _ => throw new InvalidOperationException("unused"));
        await using (var container = Build(scenario switch
        {
            "missing" => [], "incompatible" => [incompatible], _ => [registration]
        }))
        {
            var result = Assert.Throws(exceptionType, () => container.GetRequiredService<IRepository>().CommandRegistry(Key, new CommandParameters { Sql = "SELECT 1" }));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_SessionOrTransaction_UsesBorrowedResourcesAndRejectsDisposed(bool transaction)
    {
        DbConnection? captured = null;
        DbTransaction? capturedTransaction = null;
        await using (var container = Build([Register(parameters => Command(parameters, (connection, dbTransaction, _) =>
        {
            captured = connection;
                capturedTransaction = dbTransaction;
                return Task.FromResult(42);
        }))]))
        {
            var repository = container.GetRequiredService<IRepository>();
            var scope = transaction ? (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken)) : (await repository.OpenSessionAsync(TestContext.Current.CancellationToken));
            var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" });
            Assert.Equal(42, (await scope.ExecuteAsync(command, TestContext.Current.CancellationToken)));
            Assert.Equal(ConnectionState.Open, captured!.State);
            Assert.Equal(transaction, capturedTransaction is not null);
            await scope.DisposeAsync();
            await scope.DisposeAsync();
            Assert.Equal(ConnectionState.Closed, captured.State);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.ExecuteAsync(command, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("commit", 1)]
    [InlineData("rollback", 0)]
    [InlineData("dispose", 0)]
    [InlineData("failure", 0)]
    [InlineData("cancel", 0)]
    [InlineData("mapping", 0)]
    [InlineData("programming", 0)]
    public async Task Transaction_CompletionAndFailures_PersistOnlyExplicitSuccess(string completion, int expected)
    {
        await using (var container = Build([Register(parameters => Command(parameters, completion == "mapping" ?
            (_, _, _) => throw new DataException("sentinel-secret") : completion == "programming" ?
            (_, _, _) => throw new InvalidOperationException("sentinel-secret") : null))], shared: true))
        {
            var repository = container.GetRequiredService<IRepository>();
            // Keep the in-memory database alive while scopes independently own their connections.
            await using (var anchor = (await container.GetRequiredService<IRepositoryProvider>().OpenConnectionAsync(TestContext.Current.CancellationToken)))
            {
                await ExecuteRawAsync(anchor, "CREATE TABLE entries (id INTEGER)");
                var transaction = (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken));
                if (completion is "mapping" or "programming")
                {
                    var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" });
                    if (completion == "programming") await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(command, TestContext.Current.CancellationToken));
                    else
                    {
                        var failed = await Assert.ThrowsAsync<DataException>(() => transaction.ExecuteAsync(command, TestContext.Current.CancellationToken));

                        Assert.Equal("sentinel-secret", failed.Message);
                    }
                    await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                }
                else
                {
                    var inserted = await transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO entries VALUES (1)" }), TestContext.Current.CancellationToken);
                    Assert.Equal(1, inserted);
                    if (completion == "commit") await transaction.CommitAsync(TestContext.Current.CancellationToken);
                    if (completion == "rollback") await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                    if (completion is "failure" or "cancel")
                    {
                        using (var cancellation = new CancellationTokenSource())
                        {
                            if (completion == "cancel") cancellation.Cancel();
                            var failed = await Assert.ThrowsAsync(completion == "cancel" ? typeof(OperationCanceledException) : typeof(SqliteException),
                                () => transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO missing_secret VALUES (1)" }), cancellation.Token));
                            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(
                                () => transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO entries VALUES (2)" }), TestContext.Current.CancellationToken));
                            Assert.Same(failed, rejected.InnerException);
                            var commitFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                            Assert.Same(failed, commitFailure.InnerException);
                            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                        }
                    }
                }
                await transaction.DisposeAsync();
                await Assert.ThrowsAsync<ObjectDisposedException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                await using (var count = anchor.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM entries";
                    Assert.Equal(expected, Convert.ToInt32(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ConcurrentAttempt_IsRejectedAndPoisonsTransaction(bool transaction)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var container = Build([Register(parameters => Command(parameters, async (_, _, _) =>
        {
            entered.SetResult();
                await release.Task;
                return 1;
        }))]))
        {
            var repository = container.GetRequiredService<IRepository>();
            var scope = transaction ? (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken)) : (await repository.OpenSessionAsync(TestContext.Current.CancellationToken));
            await using (scope)
            {
                var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" });
                var active = scope.ExecuteAsync(command, TestContext.Current.CancellationToken);
                await entered.Task;
                var concurrent = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ExecuteAsync(command, TestContext.Current.CancellationToken));
                release.SetResult();
                await active;

                if (scope is NyssaAI.Repository.Transactions.IRepositoryTransaction atomic)
                    await Assert.ThrowsAsync<InvalidOperationException>(() => atomic.CommitAsync(TestContext.Current.CancellationToken));
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedOrWrongProvider_IsRejectedBeforeExecution()
    {
        await using (var container = Build([Register(Command)]))
        {
            var repository = container.GetRequiredService<IRepository>();
            await using (var scope = (await repository.OpenSessionAsync(TestContext.Current.CancellationToken)))
            {
                var parameters = new CommandParameters { Sql = "SELECT 1" };
                await Assert.ThrowsAsync<ArgumentException>(() => scope.ExecuteAsync(new SqlCommand(parameters, Key), TestContext.Current.CancellationToken));
                var wrong = new RepoCommandRegistry("postgresql", [RepoCommandRegistration.Create("postgresql", Key, Command)]).Resolve(Key, parameters);
                await Assert.ThrowsAsync<ArgumentException>(() => scope.ExecuteAsync(wrong, TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<ArgumentNullException>(() => scope.ExecuteAsync<CommandParameters, int>(null!, TestContext.Current.CancellationToken));
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ApplicationFailure_PreservesFirstCauseAndRollsBackEarlierWrites()
    {
        var expected = new InvalidOperationException("application failure");
        await using (var container = Build([Register(parameters => Command(parameters,
            parameters.Sql == "fail" ? (_, _, _) => throw expected : null))], shared: true))
        {
            var repository = container.GetRequiredService<IRepository>();
            var provider = container.GetRequiredService<IRepositoryProvider>();
            await using (var anchor = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken))
            {
                await ExecuteRawAsync(anchor, "CREATE TABLE entries (id INTEGER)");
                await using (var transaction = await repository.OpenTransactionAsync(TestContext.Current.CancellationToken))
                {
                    await transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO entries VALUES (1)" }), TestContext.Current.CancellationToken);
                    var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(
                        repository.CommandRegistry(Key, new CommandParameters { Sql = "fail" }), TestContext.Current.CancellationToken));
                    Assert.Same(expected, actual);
                    Assert.Contains(nameof(ExecuteAsync_ApplicationFailure_PreservesFirstCauseAndRollsBackEarlierWrites), actual.StackTrace);
                    var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                    Assert.Same(expected, rejected.InnerException);
                }
                await using (var observer = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken))
                await using (var count = observer.CreateCommand())
                {
                    count.CommandText = "SELECT COUNT(*) FROM entries";
                    Assert.Equal(0L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
                }
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ApplicationCancellationWithOtherToken_PreservesExceptionAndCause()
    {
        using (var other = new CancellationTokenSource())
        {
            var expected = new OperationCanceledException("application cancellation", other.Token);
            await using (var container = Build([Register(parameters => Command(parameters, (_, _, _) => throw expected))]))
            {
                var repository = container.GetRequiredService<IRepository>();
                await using (var transaction = await repository.OpenTransactionAsync(TestContext.Current.CancellationToken))
                {
                    var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => transaction.ExecuteAsync(
                        repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" }), TestContext.Current.CancellationToken));
                    Assert.Same(expected, actual);
                    Assert.Equal(other.Token, actual.CancellationToken);
                    var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken));
                    Assert.Same(expected, rejected.InnerException);
                    await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                }
            }
        }
    }

    [Fact]
    public async Task CommandRegistry_FactoryThrows_PreservesExceptionInstance()
    {
        var expected = new ArgumentException("factory rejected input");
        await using (var container = Build([Register(_ => throw expected)]))
        {
            var actual = Assert.Throws<ArgumentException>(() => container.GetRequiredService<IRepository>().CommandRegistry(
                Key, new CommandParameters { Sql = "unused" }));
            Assert.Same(expected, actual);
        }
    }

    private static ServiceProvider Build(IReadOnlyList<RepoCommandRegistration> registrations, bool shared = false)
    {
        var services = new ServiceCollection();
        services.AddNyssaRepository(new RepositoryConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = shared ? $"Data Source=scope-{Guid.NewGuid():N};Mode=Memory;Cache=Shared" : "Data Source=:memory:"
        }, commands: registrations);
        return services.BuildServiceProvider();
    }

    private static RepoCommandRegistration Register(Func<CommandParameters, IRepoCommand<CommandParameters, int>> create) =>
        RepoCommandRegistration.Create(" SQLITE ", Key, create);

    private static IRepoCommand<CommandParameters, int> Command(CommandParameters parameters) => Command(parameters, null);

    private static IRepoCommand<CommandParameters, int> Command(CommandParameters parameters,
        Func<DbConnection, DbTransaction?, CancellationToken, Task<int>>? execute) =>
        new SqlCommand(parameters, Key, execute);

    private static async Task ExecuteRawAsync(DbConnection connection, string sql)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed record CommandParameters
    {
        public required string Sql { get; init; }
    }

    private sealed class SqlCommand : IRepoCommand<CommandParameters, int>
    {
        private readonly Func<DbConnection, DbTransaction?, CancellationToken, Task<int>>? _execute;

        internal SqlCommand(CommandParameters parameters, RepoCommandKey<CommandParameters, int> key,
            Func<DbConnection, DbTransaction?, CancellationToken, Task<int>>? execute = null)
        {
            Parameters = parameters;
            Key = key;
            _execute = execute;
        }

        public RepoCommandKey<CommandParameters, int> Key { get; }
        public CommandParameters Parameters { get; }

        public async Task<int> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
        {
            if (_execute is not null) return await _execute(connection, transaction, cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Parameters.Sql;
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
