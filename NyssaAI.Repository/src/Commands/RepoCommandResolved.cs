using System.Data.Common;
using NyssaAI.Results;

namespace NyssaAI.Repository.Commands;

internal sealed class RepoCommandResolved<TParameters, TResult> : IRepoCommand<TParameters, TResult>
{
    private readonly IRepoCommand<TParameters, TResult> _command;

    internal RepoCommandResolved(string databaseProvider, IRepoCommand<TParameters, TResult> command)
    {
        DatabaseProvider = databaseProvider;
        _command = command;
    }

    internal string DatabaseProvider { get; }
    public RepoCommandKey<TParameters, TResult> Key => _command.Key;
    public TParameters Parameters => _command.Parameters;
    public Task<Result<TResult>> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken) =>
        _command.ExecuteCoreAsync(connection, transaction, cancellationToken);
}
