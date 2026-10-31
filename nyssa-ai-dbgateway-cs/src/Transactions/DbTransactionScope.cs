using System.Data.Common;
using NyssaAI.DbGateway.Commands;
using NyssaAI.DbGateway.Gateway;

namespace NyssaAI.DbGateway.Transactions;

/// <summary>Manages a single connection and database transaction with sequential execution guards and failure latching.</summary>
internal sealed class DbTransactionScope : IDbTransaction
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly int _commandTimeoutSeconds;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private Exception? _failure;
    private bool _isCompleted;
    private bool _isDisposed;

    internal DbTransactionScope(
        DbConnection connection,
        DbTransaction transaction,
        string databaseProvider,
        DbDialect dialect,
        int commandTimeoutSeconds)
    {
        _connection = connection;
        _transaction = transaction;
        DatabaseProvider = databaseProvider;
        Dialect = dialect;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public DbDialect Dialect { get; }

    public string DatabaseProvider { get; }

    public async Task<int> ExecuteAsync(IDbNonQuery command, CancellationToken cancellationToken = default)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken, includeFailure: true);
            return await DbCommandExecutor.ExecuteNonQueryAsync(
                _connection, _transaction, Dialect, _commandTimeoutSeconds, command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(IDbCommand<TResult> command, CancellationToken cancellationToken = default)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken, includeFailure: true);
            return await DbCommandExecutor.ExecuteCommandAsync(
                _connection, _transaction, Dialect, _commandTimeoutSeconds, command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(IDbQuery<TRow> query, CancellationToken cancellationToken = default)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken, includeFailure: true);
            return await DbCommandExecutor.ExecuteQueryAsync(
                _connection, _transaction, Dialect, _commandTimeoutSeconds, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        CompleteAsync(commit: true, cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        CompleteAsync(commit: false, cancellationToken);

    internal void RecordFailure(Exception failure)
    {
        Interlocked.CompareExchange(ref _failure, failure, null);
    }

    private async Task CompleteAsync(bool commit, CancellationToken cancellationToken)
    {
        await EnterAsync().ConfigureAwait(false);
        try
        {
            CheckReady(cancellationToken, includeFailure: commit);
            if (commit)
                await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            else
                await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _isCompleted = true;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task EnterAsync()
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false))
        {
            var failure = new InvalidOperationException("A transaction scope supports only one operation at a time.");
            RecordFailure(failure);
            throw failure;
        }
    }

    private void CheckReady(CancellationToken cancellationToken, bool includeFailure)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted)
            throw new InvalidOperationException("The transaction is already completed.");
        if (includeFailure && Volatile.Read(ref _failure) is { } failure)
            throw new InvalidOperationException("The transaction failed and must be rolled back.", failure);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            try
            {
                try
                {
                    if (!_isCompleted)
                        await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is DbException or InvalidOperationException)
                {
                }
                finally
                {
                    await _transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _operation.Release();
        }
    }
}
