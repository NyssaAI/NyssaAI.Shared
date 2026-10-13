using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.Sessions;

/// <summary>IRepositorySession executes typed commands through Dapper on one caller-owned database connection.</summary>
/// <remarks>
/// Execute commands sequentially within a scope; use independent sessions for concurrent work.
/// Queries return buffered DTOs that remain usable after disposal, without a caller-supplied row mapper.
/// Recoverable execution and materialization errors, including cancellation, return failures.
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
    /// The declared result, or an execution, materialization, cancellation, or scope-validation failure.
    /// Exactly-one-row commands return NotFound for no rows and a validation failure for multiple rows.
    /// Many-row commands return an empty buffered list when no rows match.
    /// </returns>
    public Task<Result<TResult>> ExecuteAsync<TParameters, TResult>(
        IRepoCommand<TParameters, TResult> command,
        CancellationToken cancellationToken = default);
}
