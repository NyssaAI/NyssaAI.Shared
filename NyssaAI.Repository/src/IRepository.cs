using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;

namespace NyssaAI.Repository;

/// <summary>
/// IRepository provides typed command selection and caller-owned database scopes for use-case repositories.
/// </summary>
/// <remarks>
/// The repository's configuration selects the database provider and its command registrations.
/// Each opening creates an independent scope owned by the caller and disposed with <c>await using</c>.
/// Commands execute through that scope, bind parameters, and materialize their result types.
/// The same scopes support imperative orchestration and fluent command composition.
/// </remarks>
public interface IRepository
{
    /// <summary>Creates a typed command using the registration for the configured database provider and key.</summary>
    /// <typeparam name="TParameters">The input contract fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete successful result contract fixed by the key.</typeparam>
    /// <param name="key">The logical operation and its input and result contracts.</param>
    /// <param name="parameters">The typed values the command carries and binds during execution.</param>
    /// <returns>
    /// A fresh command. Resolution does not open a connection or execute SQL.
    /// </returns>
    /// <exception cref="ArgumentException">The key or parameters are invalid; null arguments throw ArgumentNullException.</exception>
    /// <exception cref="KeyNotFoundException">No command matches the key and selected provider.</exception>
    /// <exception cref="InvalidOperationException">The registered factory or its output is incompatible.</exception>
    /// <remarks>Application factory exceptions propagate unchanged. Duplicate registrations are rejected during startup.</remarks>
    public IRepoCommand<TParameters, TResult> CommandRegistry<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key,
        TParameters parameters);

    /// <summary>Opens a connection-backed session without starting a database transaction.</summary>
    /// <param name="cancellationToken">Cancels opening the session.</param>
    /// <returns>A caller-owned session.</returns>
    /// <remarks>Driver exceptions propagate unchanged. Cancellation responsiveness depends on the driver; Npgsql startup reads may end with their original timeout even after caller cancellation.</remarks>
    /// <exception cref="ObjectDisposedException">The provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Opening is canceled; the exception carries the caller's token.</exception>
    public Task<IRepositorySession> OpenSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a connection and starts a transaction shared by all commands executed through it.</summary>
    /// <param name="cancellationToken">Cancels opening the connection or starting the transaction.</param>
    /// <returns>
    /// A caller-owned transaction. Opening failures release partially acquired resources before throwing.
    /// Disposing an uncommitted transaction attempts rollback and releases its connection; database failures can prevent rollback.
    /// </returns>
    /// <remarks>Driver exceptions propagate unchanged. Cancellation responsiveness depends on the driver; Npgsql startup reads may end with their original timeout even after caller cancellation.</remarks>
    /// <exception cref="ObjectDisposedException">The provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Opening is canceled; the exception carries the caller's token.</exception>
    public Task<IRepositoryTransaction> OpenTransactionAsync(CancellationToken cancellationToken = default);
}
