namespace NyssaAI.DbGateway.Transactions;

/// <summary>IDbTransaction executes typed commands and queries within a single shared connection and transaction boundary.</summary>
/// <remarks>
/// Execute operations sequentially within a transaction scope. Any command failure latches the transaction as failed and
/// prevents a subsequent <see cref="CommitAsync"/> from succeeding, even if the caller catches the exception.
/// Disposing an uncommitted transaction rolls back uncommitted changes and releases the underlying connection.
/// </remarks>
public interface IDbTransaction : IDbExecutor, IAsyncDisposable
{
    /// <summary>Commits all work performed within the transaction.</summary>
    /// <param name="cancellationToken">Cancels committing the transaction.</param>
    /// <returns>A task representing the commit completion.</returns>
    /// <exception cref="InvalidOperationException">The transaction is busy, already completed, or previously failed.</exception>
    /// <exception cref="ObjectDisposedException">The transaction scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">Commit is canceled.</exception>
    public Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Explicitly rolls back the transaction before disposal.</summary>
    /// <param name="cancellationToken">Cancels rolling back the transaction.</param>
    /// <returns>A task representing the rollback completion.</returns>
    /// <exception cref="InvalidOperationException">The transaction is busy or already completed.</exception>
    /// <exception cref="ObjectDisposedException">The transaction scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">Rollback is canceled.</exception>
    public Task RollbackAsync(CancellationToken cancellationToken = default);
}
