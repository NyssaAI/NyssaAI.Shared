using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
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
        var result = RepoCommandRegistration.Create(missing == "provider" ? " " : "sqlite",
            missing == "key" ? null! : Key, missing == "factory" ? null! : Command);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("parameters")]
    [InlineData("factory-result")]
    [InlineData("factory-command")]
    public async Task CommandRegistry_NullAtRuntime_ReturnsValidation(string missing)
    {
        await using var container = Build([Register(parameters => missing switch
        {
            "factory-result" => null!,
            "factory-command" => Result<IRepoCommand<CommandParameters, int>>.Success(null!),
            _ => Command(parameters)
        })]);
        var repository = container.GetRequiredService<IRepository>();
        var result = repository.CommandRegistry(missing == "key" ? null! : Key,
            missing == "parameters" ? null! : new CommandParameters { Sql = "unused" });
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task ExecuteAsync_CommandFromAnotherRepositoryWithSameProvider_IsCompatible()
    {
        await using var first = Build([Register(Command)]);
        await using var second = Build([]);
        var command = first.GetRequiredService<IRepository>().CommandRegistry(Key, new CommandParameters { Sql = "SELECT 1" }).Value;
        await using var session = (await second.GetRequiredService<IRepository>().OpenSessionAsync(TestContext.Current.CancellationToken)).Value;
        Assert.True((await session.ExecuteAsync(command, TestContext.Current.CancellationToken)).IsSuccess);
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
        await using var container = services.BuildServiceProvider();
        var repository = container.GetRequiredService<IRepository>();
        var parameters = new CommandParameters { Sql = "SELECT 1" };
        Assert.Equal(0, calls);
        var first = repository.CommandRegistry(Key, parameters);
        var second = repository.CommandRegistry(Key, parameters);
        Assert.True(first.IsSuccess);
        Assert.Equal(2, calls);
        Assert.NotSame(first.Value, second.Value);
        Assert.Same(parameters, first.Value.Parameters);
    }

    [Theory]
    [InlineData("missing", ErrorCategory.NotFound)]
    [InlineData("duplicate", ErrorCategory.Conflict)]
    [InlineData("incompatible", ErrorCategory.Validation)]
    [InlineData("parameters", ErrorCategory.Validation)]
    [InlineData("key", ErrorCategory.Validation)]
    [InlineData("validation", ErrorCategory.Validation)]
    public async Task CommandRegistry_InvalidRegistration_ReturnsFailure(string scenario, ErrorCategory category)
    {
        var registration = Register(parameters => scenario switch
        {
            "parameters" => Command(parameters with { Sql = "SELECT 2" }),
            "key" => Result<IRepoCommand<CommandParameters, int>>.Success(new SqlCommand(parameters, new() { Name = "other" })),
            "validation" => Result<IRepoCommand<CommandParameters, int>>.Failure(Error.FromMessage("Rejected parameters", ErrorCategory.Validation)),
            _ => Command(parameters)
        });
        var incompatible = RepoCommandRegistration.Create("sqlite", new RepoCommandKey<string, int> { Name = Key.Name },
            _ => Result<IRepoCommand<string, int>>.Failure("unused")).Value;
        await using var container = Build(scenario switch
        {
            "missing" => [], "duplicate" => [registration, registration], "incompatible" => [incompatible], _ => [registration]
        });
        var result = container.GetRequiredService<IRepository>().CommandRegistry(Key, new CommandParameters { Sql = "SELECT 1" });
        Assert.Equal(category, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_SessionOrTransaction_UsesBorrowedResourcesAndRejectsDisposed(bool transaction)
    {
        DbConnection? captured = null;
        DbTransaction? capturedTransaction = null;
        await using var container = Build([Register(parameters => Command(parameters, (connection, dbTransaction, _) =>
        {
            captured = connection;
            capturedTransaction = dbTransaction;
            return Task.FromResult(Result<int>.Success(42));
        }))]);
        var repository = container.GetRequiredService<IRepository>();
        var scope = transaction ? (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken)).Value : (await repository.OpenSessionAsync(TestContext.Current.CancellationToken)).Value;
        var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" }).Value;
        Assert.Equal(42, (await scope.ExecuteAsync(command, TestContext.Current.CancellationToken)).Value);
        Assert.Equal(ConnectionState.Open, captured!.State);
        Assert.Equal(transaction, capturedTransaction is not null);
        await scope.DisposeAsync();
        await scope.DisposeAsync();
        Assert.Equal(ConnectionState.Closed, captured.State);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single((await scope.ExecuteAsync(command, TestContext.Current.CancellationToken)).Errors).Category);
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
        await using var container = Build([Register(parameters => Command(parameters, completion == "mapping" ?
            (_, _, _) => throw new DataException("sentinel-secret") : completion == "programming" ?
            (_, _, _) => throw new InvalidOperationException("sentinel-secret") : null))], shared: true);
        var repository = container.GetRequiredService<IRepository>();
        // Keep the in-memory database alive while scopes independently own their connections.
        await using var anchor = (await container.GetRequiredService<IRepositoryProvider>().OpenConnectionAsync(TestContext.Current.CancellationToken)).Value;
        await ExecuteRawAsync(anchor, "CREATE TABLE entries (id INTEGER)");
        var transaction = (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken)).Value;
        if (completion is "mapping" or "programming")
        {
            var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" }).Value;
            if (completion == "programming") await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(command, TestContext.Current.CancellationToken));
            else
            {
                var failed = await transaction.ExecuteAsync(command, TestContext.Current.CancellationToken);
                Assert.Equal(ErrorCategory.Validation, Assert.Single(failed.Errors).Category);
                Assert.DoesNotContain("sentinel-secret", failed.ToString());
            }
            Assert.True((await transaction.CommitAsync(TestContext.Current.CancellationToken)).IsFailure);
        }
        else
        {
            var inserted = await transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO entries VALUES (1)" }).Value, TestContext.Current.CancellationToken);
            Assert.True(inserted.IsSuccess);
            if (completion == "commit") Assert.True((await transaction.CommitAsync(TestContext.Current.CancellationToken)).IsSuccess);
            if (completion == "rollback") Assert.True((await transaction.RollbackAsync(TestContext.Current.CancellationToken)).IsSuccess);
            if (completion is "failure" or "cancel")
            {
                using var cancellation = new CancellationTokenSource();
                if (completion == "cancel") cancellation.Cancel();
                var failed = await transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO missing_secret VALUES (1)" }).Value, cancellation.Token);
                Assert.True(failed.IsFailure);
                Assert.DoesNotContain("missing_secret", failed.ToString());
                var blocked = await transaction.ExecuteAsync(repository.CommandRegistry(Key, new CommandParameters { Sql = "INSERT INTO entries VALUES (2)" }).Value, TestContext.Current.CancellationToken);
                Assert.Equal(failed.Errors, blocked.Errors);
                Assert.Equal(failed.Errors, (await transaction.CommitAsync(TestContext.Current.CancellationToken)).Errors);
                Assert.True((await transaction.RollbackAsync(TestContext.Current.CancellationToken)).IsSuccess);
            }
        }
        await transaction.DisposeAsync();
        Assert.True((await transaction.CommitAsync(TestContext.Current.CancellationToken)).IsFailure);
        await using var count = anchor.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM entries";
        Assert.Equal(expected, Convert.ToInt32(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ConcurrentAttempt_IsRejectedAndPoisonsTransaction(bool transaction)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var container = Build([Register(parameters => Command(parameters, async (_, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return Result<int>.Success(1);
        }))]);
        var repository = container.GetRequiredService<IRepository>();
        var scope = transaction ? (await repository.OpenTransactionAsync(TestContext.Current.CancellationToken)).Value : (await repository.OpenSessionAsync(TestContext.Current.CancellationToken)).Value;
        await using (scope)
        {
            var command = repository.CommandRegistry(Key, new CommandParameters { Sql = "unused" }).Value;
            var active = scope.ExecuteAsync(command, TestContext.Current.CancellationToken);
            await entered.Task;
            var concurrent = await scope.ExecuteAsync(command, TestContext.Current.CancellationToken);
            release.SetResult();
            Assert.True((await active).IsSuccess);
            Assert.Equal(ErrorCategory.Conflict, Assert.Single(concurrent.Errors).Category);
            if (scope is NyssaAI.Repository.Transactions.IRepositoryTransaction atomic)
                Assert.True((await atomic.CommitAsync(TestContext.Current.CancellationToken)).IsFailure);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedOrWrongProvider_IsRejectedBeforeExecution()
    {
        await using var container = Build([Register(Command)]);
        var repository = container.GetRequiredService<IRepository>();
        await using var scope = (await repository.OpenSessionAsync(TestContext.Current.CancellationToken)).Value;
        var parameters = new CommandParameters { Sql = "SELECT 1" };
        Assert.True((await scope.ExecuteAsync(new SqlCommand(parameters, Key), TestContext.Current.CancellationToken)).IsFailure);
        var wrong = new RepoCommandRegistry("postgresql", [RepoCommandRegistration.Create("postgresql", Key, Command).Value]).Resolve(Key, parameters);
        Assert.True((await scope.ExecuteAsync(wrong.Value, TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await scope.ExecuteAsync<CommandParameters, int>(null!, TestContext.Current.CancellationToken)).IsFailure);
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

    private static RepoCommandRegistration Register(Func<CommandParameters, Result<IRepoCommand<CommandParameters, int>>> create) =>
        RepoCommandRegistration.Create(" SQLITE ", Key, create).Value;

    private static Result<IRepoCommand<CommandParameters, int>> Command(CommandParameters parameters) => Command(parameters, null);

    private static Result<IRepoCommand<CommandParameters, int>> Command(CommandParameters parameters,
        Func<DbConnection, DbTransaction?, CancellationToken, Task<Result<int>>>? execute) =>
        Result<IRepoCommand<CommandParameters, int>>.Success(new SqlCommand(parameters, Key, execute));

    private static async Task ExecuteRawAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed record CommandParameters
    {
        public required string Sql { get; init; }
    }

    private sealed class SqlCommand : IRepoCommand<CommandParameters, int>
    {
        private readonly Func<DbConnection, DbTransaction?, CancellationToken, Task<Result<int>>>? _execute;

        internal SqlCommand(CommandParameters parameters, RepoCommandKey<CommandParameters, int> key,
            Func<DbConnection, DbTransaction?, CancellationToken, Task<Result<int>>>? execute = null)
        {
            Parameters = parameters;
            Key = key;
            _execute = execute;
        }

        public RepoCommandKey<CommandParameters, int> Key { get; }
        public CommandParameters Parameters { get; }

        public async Task<Result<int>> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
        {
            if (_execute is not null) return await _execute(connection, transaction, cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Parameters.Sql;
            return Result<int>.Success(await command.ExecuteNonQueryAsync(cancellationToken));
        }
    }
}
