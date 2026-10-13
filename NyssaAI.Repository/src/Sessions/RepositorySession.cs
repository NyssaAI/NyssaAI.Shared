using System.Data;
using System.Data.Common;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.Sessions;

internal sealed class RepositorySession : IRepositorySession, IRepositoryScopeFailure
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;
    private readonly string _databaseProvider;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private Result? _failure;
    private bool _isCompleted;
    private bool _isDisposed;

    internal RepositorySession(DbConnection connection, string databaseProvider, DbTransaction? transaction = null)
    {
        _connection = connection;
        _databaseProvider = databaseProvider;
        _transaction = transaction;
    }

    public void RecordFailure(Result failure)
    {
        if (_transaction is not null && failure.IsFailure) Interlocked.CompareExchange(ref _failure, failure, null);
    }

    Result IRepositoryScopeFailure.CheckReady(CancellationToken cancellationToken) => CheckReady(cancellationToken);

    public async Task<Result<TResult>> ExecuteAsync<TParameters, TResult>(IRepoCommand<TParameters, TResult> command, CancellationToken cancellationToken = default)
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false))
            return Reject<TResult>(Failure("A scope supports only one operation at a time.", ErrorCategory.Conflict));
        var didFinish = false;
        try
        {
            var ready = CheckReady(cancellationToken);
            if (ready.IsFailure) return Reject<TResult>(ready);
            if (command is not RepoCommandResolved<TParameters, TResult> resolved || resolved.DatabaseProvider != _databaseProvider)
                return Reject<TResult>(Failure("The command was not resolved for this scope's provider.", ErrorCategory.Validation));
            var result = await command.ExecuteCoreAsync(_connection, _transaction, cancellationToken).ConfigureAwait(false);
            if (result is null) return Reject<TResult>(Failure("The command returned no outcome.", ErrorCategory.Unexpected));
            if (result.IsFailure) RecordFailure(Result.Failure(result.Errors));
            didFinish = true;
            return result;
        }
        catch (Exception exception) when (exception is DbException or DataException or OperationCanceledException)
        {
            didFinish = true;
            return Reject<TResult>(Translate(exception));
        }
        finally
        {
            // An escaping author/programming exception must never leave a transaction committable.
            if (!didFinish) RecordFailure(Failure("Command execution did not complete successfully.", ErrorCategory.Unexpected));
            _operation.Release();
        }
    }

    internal async Task<Result> CompleteAsync(bool commit, CancellationToken cancellationToken)
    {
        if (!await _operation.WaitAsync(0).ConfigureAwait(false))
        {
            var busy = Failure("A scope supports only one operation at a time.", ErrorCategory.Conflict);
            RecordFailure(busy);
            return busy;
        }
        var didFinish = false;
        try
        {
            var ready = CheckReady(cancellationToken, includeFailure: commit);
            if (ready.IsFailure)
            {
                RecordFailure(ready);
                return ready;
            }
            // Only RepositoryTransaction calls completion; its construction always supplies a database transaction.
            if (commit) await _transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
            else await _transaction!.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _isCompleted = true;
            didFinish = true;
            return Result.Success();
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            var failure = Translate(exception);
            RecordFailure(failure);
            didFinish = true;
            return failure;
        }
        finally
        {
            if (!didFinish) RecordFailure(Failure("Transaction completion did not succeed.", ErrorCategory.Unexpected));
            _operation.Release();
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

    private Result CheckReady(CancellationToken cancellationToken, bool includeFailure = true)
    {
        if (_isDisposed || _isCompleted) return Failure("The scope is already completed or disposed.", ErrorCategory.Conflict);
        if (includeFailure && _failure is not null) return _failure;
        if (cancellationToken.IsCancellationRequested) return Failure("Repository execution was canceled.", ErrorCategory.Canceled);
        return Result.Success();
    }

    private Result<T> Reject<T>(Result failure)
    {
        RecordFailure(failure);
        return Result<T>.Failure(failure.Errors);
    }

    internal static Result Translate(Exception exception) => Failure("The database operation could not be completed.",
        exception is OperationCanceledException ? ErrorCategory.Canceled : exception is DataException ? ErrorCategory.Validation : ErrorCategory.Unavailable);

    private static Result Failure(string message, ErrorCategory category) => Result.Failure(Error.FromMessage(message, category));
}
