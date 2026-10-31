using NyssaAI.DbGateway.Commands;

namespace NyssaAI.DbGateway;

/// <summary>Executes reusable typed database commands and queries with dialect-aware parameter and row coercion.</summary>
public interface IDbExecutor
{
    /// <summary>Gets the active SQL dialect of the underlying database provider.</summary>
    public DbDialect Dialect { get; }

    /// <summary>Gets the canonical lowercase provider name.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Executes a non-query write command and returns the number of affected rows.</summary>
    /// <param name="command">The typed write operation carrying its parameter values.</param>
    /// <param name="cancellationToken">Cancels execution.</param>
    /// <returns>The number of rows affected by the command.</returns>
    /// <exception cref="ArgumentNullException">The supplied command is null.</exception>
    /// <exception cref="ArgumentException">The command produced empty or invalid SQL.</exception>
    /// <exception cref="InvalidOperationException">The scope is busy, completed, or previously failed.</exception>
    /// <exception cref="ObjectDisposedException">The gateway or transaction scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task<int> ExecuteAsync(IDbNonQuery command, CancellationToken cancellationToken = default);

    /// <summary>Executes a single-result command and returns its materialized result.</summary>
    /// <typeparam name="TResult">The result type produced by the command.</typeparam>
    /// <param name="command">The typed command carrying its parameter values and result mapping.</param>
    /// <param name="cancellationToken">Cancels execution and reading.</param>
    /// <returns>The materialized command result.</returns>
    /// <exception cref="ArgumentNullException">The supplied command is null.</exception>
    /// <exception cref="ArgumentException">The command produced empty or invalid SQL.</exception>
    /// <exception cref="InvalidOperationException">The scope is busy, completed, or previously failed.</exception>
    /// <exception cref="ObjectDisposedException">The gateway or transaction scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task<TResult> ExecuteAsync<TResult>(IDbCommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>Executes a multi-row query and buffers all mapped rows into an immutable list.</summary>
    /// <typeparam name="TRow">The row type mapped from each record.</typeparam>
    /// <param name="query">The typed query carrying its parameter values and row mapper.</param>
    /// <param name="cancellationToken">Cancels execution and reading.</param>
    /// <returns>The buffered list of mapped rows.</returns>
    /// <exception cref="ArgumentNullException">The supplied query is null.</exception>
    /// <exception cref="ArgumentException">The query produced empty or invalid SQL.</exception>
    /// <exception cref="InvalidOperationException">The scope is busy, completed, or previously failed.</exception>
    /// <exception cref="ObjectDisposedException">The gateway or transaction scope is disposed.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(IDbQuery<TRow> query, CancellationToken cancellationToken = default);
}
