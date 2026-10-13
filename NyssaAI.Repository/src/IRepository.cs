using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;
using NyssaAI.Results;

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
    /// A fresh command, or a validation failure for an invalid key or values, or a missing,
    /// duplicate, or incompatible registration.
    /// Resolution does not open a connection or execute SQL.
    /// </returns>
    public Result<IRepoCommand<TParameters, TResult>> CommandRegistry<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key,
        TParameters parameters);

    /// <summary>Opens a connection-backed session without starting a database transaction.</summary>
    /// <param name="cancellationToken">Cancels opening the session.</param>
    /// <returns>A caller-owned session, or a failure if opening fails or is canceled.</returns>
    public Task<Result<IRepositorySession>> OpenSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a connection and starts a transaction shared by all commands executed through it.</summary>
    /// <param name="cancellationToken">Cancels opening the connection or starting the transaction.</param>
    /// <returns>
    /// A caller-owned transaction, or a failure with partially opened resources released.
    /// Disposing a transaction without a successful commit rolls it back and releases its connection.
    /// </returns>
    public Task<Result<IRepositoryTransaction>> OpenTransactionAsync(CancellationToken cancellationToken = default);
}
