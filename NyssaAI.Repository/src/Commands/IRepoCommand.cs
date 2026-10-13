using System.Data.Common;
using NyssaAI.Results;

namespace NyssaAI.Repository.Commands;

/// <summary>IRepoCommand carries a typed operation key and the parameter values supplied for execution.</summary>
/// <typeparam name="TParameters">The operation's input contract.</typeparam>
/// <typeparam name="TResult">The complete successful result returned by the operation.</typeparam>
/// <remarks>
/// Execute the command through a caller-owned session or transaction for the provider that resolved it.
/// A command owns no connection or transaction. Its implementation supplies trusted provider-specific SQL;
/// The implementation binds parameter values separately and materializes rows into concrete DTOs.
/// TResult can describe a count, scalar, DTO, buffered list, or supported composite result.
/// For a list result, Dapper's row type is the element DTO, not the list itself.
/// Any implementation-side execution hook remains subject to its database library and target-runtime validation;
/// this contract does not require a separate executor or public execution context.
/// </remarks>
public interface IRepoCommand<TParameters, TResult>
{
    /// <summary>Gets the logical operation key fixing this command's input and result contracts.</summary>
    public RepoCommandKey<TParameters, TResult> Key { get; }

    /// <summary>Gets the typed values supplied during command resolution and bound at execution.</summary>
    public TParameters Parameters { get; }

    /// <summary>Executes this trusted command using resources borrowed from its owning scope.</summary>
    /// <remarks>Callers execute through the session. Implementations must not retain, dispose, or complete these resources.</remarks>
    /// <param name="connection">The scope's open connection.</param>
    /// <param name="transaction">The active transaction, or null for a standalone session.</param>
    /// <param name="cancellationToken">Cancels execution and materialization.</param>
    /// <returns>The complete buffered result or a domain failure.</returns>
    public Task<Result<TResult>> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken);
}
