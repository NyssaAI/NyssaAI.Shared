using NyssaAI.Repository.Sessions;

namespace NyssaAI.Repository.Transactions;

/// <summary>IRepositoryTransaction executes typed commands on one connection in a caller-owned transaction.</summary>
/// <remarks>
/// Inherited typed execution enlists every database operation in this transaction. The first command failure
/// prevents further command execution and commit, even if the caller catches the exception.
/// A command pipeline stops at that failure. Successful commands may supply inputs for later commands.
/// Disposal of uncommitted work attempts rollback and releases the connection. Database failures can prevent rollback. A completed transaction
/// rejects further execution. Rollback and disposal must still be possible after cancellation.
/// Commands do not own the transaction or decide when to commit or roll back. Any implementation-side
/// command execution hook must preserve this scope's lifecycle checks and failure tracking.
/// For reliable external notifications, include an outbox insert in this transaction and publish
/// it separately after commit; an external queue is outside this database transaction.
/// </remarks>
public interface IRepositoryTransaction : IRepositorySession
{
    /// <summary>Commits the transaction only if all commands executed through it succeeded.</summary>
    /// <param name="cancellationToken">Cancels the commit request.</param>
    /// <returns>A task that completes after commit.</returns>
    /// <exception cref="InvalidOperationException">Prior work failed, or the scope is completed or busy. A previous failure is retained as InnerException.</exception>
    /// <remarks>Driver exceptions propagate unchanged.</remarks>
    /// <exception cref="ObjectDisposedException">The scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">The current commit request is canceled. A previously canceled operation instead leaves the transaction failed.</exception>
    public Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Explicitly rolls back the transaction before disposal.</summary>
    /// <param name="cancellationToken">Cancels the explicit rollback request; disposal still performs cleanup.</param>
    /// <returns>A task that completes after rollback.</returns>
    /// <exception cref="InvalidOperationException">The scope is completed or busy.</exception>
    /// <remarks>Driver exceptions propagate unchanged.</remarks>
    /// <exception cref="ObjectDisposedException">The scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">The rollback request is canceled. Use a fresh token after canceled work.</exception>
    public Task RollbackAsync(CancellationToken cancellationToken = default);
}
