using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.Tests.Transactions;

public sealed class RepositoryTransactionOpeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenTransactionAsync_BeginFails_ReleasesPartiallyOpenedConnection(bool canceled)
    {
        var connection = new TransactionConnection(canceled);
        var provider = new TransactionProvider(connection);
        var services = new ServiceCollection();
        services.AddNyssaRepository(new RepositoryConfiguration { DatabaseProvider = "controlled", ConnectionString = "unused" },
            providers: [new RepositoryProviderRegistration
            {
                DatabaseProvider = "controlled",
                Prepare = _ => Result<Func<IRepositoryProvider>>.Success(() => provider)
            }]);
        await using var container = services.BuildServiceProvider();
        var result = await container.GetRequiredService<IRepository>().OpenTransactionAsync(TestContext.Current.CancellationToken);
        Assert.True(connection.IsDisposed);
        Assert.Equal(canceled ? ErrorCategory.Canceled : ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
        Assert.DoesNotContain("sentinel-secret", result.ToString());
    }

    private sealed class TransactionProvider : IRepositoryProvider
    {
        private readonly DbConnection _connection;
        internal TransactionProvider(DbConnection connection) => _connection = connection;
        public string DatabaseProvider => "controlled";
        public Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<DbConnection>.Success(_connection));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TransactionConnection : DbConnection
    {
        private readonly bool _isCanceled;
        internal TransactionConnection(bool isCanceled) => _isCanceled = isCanceled;
        internal bool IsDisposed { get; private set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "unused";
        public override string Database => "controlled";
        public override string DataSource => "controlled";
        public override string ServerVersion => "1";
        public override ConnectionState State => IsDisposed ? ConnectionState.Closed : ConnectionState.Open;
        public override void Open() { }
        public override void Close() => IsDisposed = true;
        public override void ChangeDatabase(string databaseName) { }
        protected override DbCommand CreateDbCommand() => throw new InvalidOperationException("No command is expected while beginning a transaction.");
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            _isCanceled ? throw new OperationCanceledException("sentinel-secret") : throw new TransactionBeginException();
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TransactionBeginException : DbException
    {
        internal TransactionBeginException() : base("sentinel-secret") { }
    }
}
