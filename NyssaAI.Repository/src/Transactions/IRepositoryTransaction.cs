using NyssaAI.Repository.Sessions;
using NyssaAI.Results;

namespace NyssaAI.Repository.Transactions;

/// <summary>IRepositoryTransaction executes typed commands on one connection in a caller-owned transaction.</summary>
/// <remarks>
/// Inherited typed execution enlists every database operation in this transaction. The first command failure
/// prevents further command execution and commit, even if the caller ignores the returned failure.
/// A command pipeline stops at that failure. Successful commands may supply inputs for later commands.
/// Disposal without successful commit rolls back and releases the connection. A completed transaction
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
    /// <returns>Success when committed, or the command, commit, cancellation, or lifecycle failure.</returns>
    public Task<Result> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Explicitly rolls back the transaction before disposal.</summary>
    /// <param name="cancellationToken">Cancels the explicit rollback request; disposal still performs cleanup.</param>
    /// <returns>Success when rolled back, or a rollback, cancellation, or lifecycle failure.</returns>
    public Task<Result> RollbackAsync(CancellationToken cancellationToken = default);
}
