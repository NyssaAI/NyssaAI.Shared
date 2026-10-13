using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.Tests.Transactions;

public sealed class RepositoryTransactionCompletionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task CompleteAsync_ProviderFailure_PreservesFailedStateAndCleanup(bool commit, bool canceled)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var driverTransaction = new CompletionTransaction(connection, commit, canceled);
        var scope = new RepositoryTransaction(new RepositorySession(connection, "sqlite", driverTransaction));
        await using (scope)
        {
            var failed = commit ? await scope.CommitAsync(TestContext.Current.CancellationToken) : await scope.RollbackAsync(TestContext.Current.CancellationToken);
            Assert.Equal(canceled ? ErrorCategory.Canceled : ErrorCategory.Unavailable, Assert.Single(failed.Errors).Category);
            Assert.DoesNotContain("sentinel-secret", failed.ToString());
            var commitCalls = driverTransaction.CommitCalls;
            Assert.Equal(failed.Errors, (await scope.CommitAsync(TestContext.Current.CancellationToken)).Errors);
            Assert.Equal(commitCalls, driverTransaction.CommitCalls);
            driverTransaction.FailCompletion = false;
            Assert.True((await scope.RollbackAsync(TestContext.Current.CancellationToken)).IsSuccess);
            Assert.True((await scope.CommitAsync(TestContext.Current.CancellationToken)).IsFailure);
        }
        Assert.True(driverTransaction.IsDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private sealed class CompletionTransaction : DbTransaction
    {
        private readonly DbConnection _connection;
        private readonly bool _failCommit;
        private readonly bool _canceled;
        internal CompletionTransaction(DbConnection connection, bool failCommit, bool canceled)
        {
            _connection = connection;
            _failCommit = failCommit;
            _canceled = canceled;
        }
        internal bool FailCompletion { get; set; } = true;
        internal int CommitCalls { get; private set; }
        internal bool IsDisposed { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;
        protected override DbConnection DbConnection => _connection;
        public override void Commit() { CommitCalls++; Complete(true); }
        public override void Rollback() => Complete(false);
        public override ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
        private void Complete(bool commit)
        {
            if (!FailCompletion || commit != _failCommit) return;
            if (_canceled) throw new OperationCanceledException("sentinel-secret");
            throw new CompletionException();
        }
    }

    private sealed class CompletionException : DbException
    {
        internal CompletionException() : base("sentinel-secret") { }
    }
}
