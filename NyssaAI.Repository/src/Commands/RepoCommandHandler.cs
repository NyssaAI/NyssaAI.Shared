using System.Data.Common;

namespace NyssaAI.Repository.Commands;

/// <summary>Executes a typed command delegate using resources borrowed from its owning session or transaction.</summary>
/// <typeparam name="TParameters">The operation's input contract.</typeparam>
/// <typeparam name="TResult">The complete successful result returned by the operation.</typeparam>
/// <param name="parameters">The typed values supplied during command resolution.</param>
/// <param name="connection">The scope's open connection.</param>
/// <param name="transaction">The active transaction, or null for a standalone session.</param>
/// <param name="cancellationToken">Cancels execution and materialization.</param>
/// <returns>The complete buffered value. Throw on execution or domain failure.</returns>
public delegate Task<TResult> RepoCommandHandler<in TParameters, TResult>(
    TParameters parameters,
    DbConnection connection,
    DbTransaction? transaction,
    CancellationToken cancellationToken);
