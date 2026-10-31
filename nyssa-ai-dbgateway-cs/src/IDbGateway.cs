using NyssaAI.DbGateway.Transactions;

namespace NyssaAI.DbGateway;

/// <summary>IDbGateway provides low-ceremony, AOT-safe typed command execution and transactions across SQLite and PostgreSQL.</summary>
/// <remarks>
/// Standalone calls on <see cref="IDbGateway"/> open an independent connection for the operation and dispose it upon completion.
/// Multi-step atomic workflows use <see cref="ExecuteTransactionAsync(Func{IDbTransaction, CancellationToken, Task}, CancellationToken)"/>
/// or <see cref="OpenTransactionAsync(CancellationToken)"/> to share a single connection and database transaction.
/// </remarks>
public interface IDbGateway : IDbExecutor
{
    /// <summary>Opens a connection, starts a transaction, executes the callback, and commits on completion or rolls back on failure.</summary>
    /// <param name="work">The asynchronous transactional workflow receiving the active transaction scope.</param>
    /// <param name="cancellationToken">Cancels connection opening, command execution, or commit.</param>
    /// <returns>A task representing the completed and committed transaction.</returns>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    /// <exception cref="InvalidOperationException">A command within the transaction failed or left the transaction unable to commit.</exception>
    /// <exception cref="ObjectDisposedException">The underlying provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task ExecuteTransactionAsync(
        Func<IDbTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a connection, starts a transaction, executes the callback, commits on completion, and returns the callback's result.</summary>
    /// <typeparam name="TResult">The result type returned by the transactional workflow.</typeparam>
    /// <param name="work">The asynchronous transactional workflow receiving the active transaction scope.</param>
    /// <param name="cancellationToken">Cancels connection opening, command execution, or commit.</param>
    /// <returns>The value returned by the callback after the transaction commits.</returns>
    /// <exception cref="ArgumentNullException">The callback is null.</exception>
    /// <exception cref="InvalidOperationException">A command within the transaction failed or left the transaction unable to commit.</exception>
    /// <exception cref="ObjectDisposedException">The underlying provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<IDbTransaction, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a caller-owned connection and transaction for explicit commit or rollback control.</summary>
    /// <param name="cancellationToken">Cancels connection opening or transaction start.</param>
    /// <returns>A caller-owned transaction scope that rolls back uncommitted work on disposal.</returns>
    /// <exception cref="ObjectDisposedException">The underlying provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Opening is canceled.</exception>
    public Task<IDbTransaction> OpenTransactionAsync(CancellationToken cancellationToken = default);
}
