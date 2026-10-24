using System.Data.Common;

namespace NyssaAI.Repository.Commands;

internal sealed class RepoCommandDelegate<TParameters, TResult> : IRepoCommand<TParameters, TResult>
{
    private readonly RepoCommandHandler<TParameters, TResult> _execute;

    internal RepoCommandDelegate(
        RepoCommandKey<TParameters, TResult> key,
        TParameters parameters,
        RepoCommandHandler<TParameters, TResult> execute)
    {
        Key = key;
        Parameters = parameters;
        _execute = execute;
    }

    public RepoCommandKey<TParameters, TResult> Key { get; }

    public TParameters Parameters { get; }

    public Task<TResult> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken) =>
        _execute(Parameters, connection, transaction, cancellationToken);
}
