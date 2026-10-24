using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.Sessions;

/// <summary>IRepositorySession executes typed commands on one caller-owned database connection.</summary>
/// <remarks>
/// Execute commands sequentially within a scope; use independent sessions for concurrent work.
/// Queries return buffered DTOs that remain usable after disposal, without a caller-supplied row mapper.
/// Recoverable execution and materialization errors, including cancellation, throw exceptions.
/// Execute only commands resolved for this scope's provider; incompatible or disposed scopes reject execution.
/// The session forwards cancellation and owns execution checks and cleanup, including when it invokes
/// a concrete command's implementation-side execution method. Callers do not invoke that method directly.
/// A session without a transaction does not make multiple commands atomic.
/// Disposal releases the connection.
/// </remarks>
public interface IRepositorySession : IAsyncDisposable
{
    /// <summary>Executes the resolved command and returns the complete successful result fixed by its key.</summary>
    /// <typeparam name="TParameters">The command's input contract.</typeparam>
    /// <typeparam name="TResult">The complete result: a count, scalar, DTO, buffered list, or supported composite.</typeparam>
    /// <param name="command">The resolved typed command carrying its parameter values.</param>
    /// <param name="cancellationToken">Cancels execution and reading.</param>
    /// <returns>
    /// The declared value directly. The application command defines empty-result and row-cardinality behavior.
    /// </returns>
    /// <exception cref="InvalidOperationException">The scope is busy, completed, or its transaction previously failed. A previous failure is retained as InnerException.</exception>
    /// <exception cref="ObjectDisposedException">The scope is disposed.</exception>
    /// <exception cref="ArgumentException">The command is null or incompatible with the provider.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled. Application cancellation exceptions retain their own tokens.</exception>
    /// <remarks>Driver, mapping, and application exceptions propagate unchanged. Any failed execution through a transaction prevents later commit, even if caught.</remarks>
    public Task<TResult> ExecuteAsync<TParameters, TResult>(
        IRepoCommand<TParameters, TResult> command,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a typed command for the scope's provider and executes it in a single call.</summary>
    /// <typeparam name="TParameters">The command's input contract fixed by the key.</typeparam>
    /// <typeparam name="TResult">The complete result: a count, scalar, DTO, buffered list, or supported composite.</typeparam>
    /// <param name="key">The logical operation and its input and result contracts.</param>
    /// <param name="parameters">The typed values the command carries and binds during execution.</param>
    /// <param name="cancellationToken">Cancels resolution, execution, and reading.</param>
    /// <returns>
    /// The declared value directly. The application command defines empty-result and row-cardinality behavior.
    /// </returns>
    /// <exception cref="InvalidOperationException">The scope is busy, completed, its transaction previously failed, or the registered factory is incompatible.</exception>
    /// <exception cref="ObjectDisposedException">The scope is disposed.</exception>
    /// <exception cref="ArgumentException">The key or parameters are invalid; null arguments throw ArgumentNullException.</exception>
    /// <exception cref="KeyNotFoundException">No command matches the key and selected provider.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled. Application cancellation exceptions retain their own tokens.</exception>
    /// <remarks>Driver, mapping, factory, and application exceptions propagate unchanged. Any failed resolution or execution through a transaction prevents later commit, even if caught.</remarks>
    public Task<TResult> ExecuteAsync<TParameters, TResult>(
        RepoCommandKey<TParameters, TResult> key,
        TParameters parameters,
        CancellationToken cancellationToken = default);
}
