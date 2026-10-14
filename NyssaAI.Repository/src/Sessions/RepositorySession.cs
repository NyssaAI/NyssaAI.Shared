using System.Data.Common;
using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.Sessions;

internal sealed class RepositorySession : IRepositorySession, IRepositoryScopeFailure
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;
    private readonly string _databaseProvider;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private Exception? _failure;
    private bool _isCompleted;
    private bool _isDisposed;

    internal RepositorySession(DbConnection connection, string databaseProvider, DbTransaction? transaction = null)
    {
        _connection = connection;
        _databaseProvider = databaseProvider;
        _transaction = transaction;
    }

    public void RecordFailure(Exception failure)
    {
        if (_transaction is not null) Interlocked.CompareExchange(ref _failure, failure, null);
    }

    void IRepositoryScopeFailure.CheckReady(CancellationToken cancellationToken) => CheckReady(cancellationToken);

    public async Task<TResult> ExecuteAsync<TParameters, TResult>(IRepoCommand<TParameters, TResult> command, CancellationToken cancellationToken = default)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken);
            ArgumentNullException.ThrowIfNull(command);
            if (command is not RepoCommandResolved<TParameters, TResult> resolved || resolved.DatabaseProvider != _databaseProvider)
                throw new ArgumentException("The command was not resolved for this scope's provider.", nameof(command));
            return await command.ExecuteCoreAsync(_connection, _transaction, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally { _operation.Release(); }
    }

    internal async Task CompleteAsync(bool commit, CancellationToken cancellationToken)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken, includeFailure: commit);
            if (commit) await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
            else await _transaction!.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _isCompleted = true;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally { _operation.Release(); }
    }

    private async Task EnterAsync()
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false))
        {
            var failure = new InvalidOperationException("A scope supports only one operation at a time.");
            RecordFailure(failure);
            throw failure;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                if (_transaction is not null)
                {
                    try
                    {
                        if (!_isCompleted) await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is DbException or InvalidOperationException) { }
                    finally { await _transaction.DisposeAsync().ConfigureAwait(false); }
                }
            }
            finally { await _connection.DisposeAsync().ConfigureAwait(false); }
        }
        finally { _operation.Release(); }
    }

    private void CheckReady(CancellationToken cancellationToken, bool includeFailure = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted) throw new InvalidOperationException("The scope is already completed.");
        if (includeFailure && Volatile.Read(ref _failure) is { } failure)
            throw new InvalidOperationException("The transaction failed and must be rolled back.", failure);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
