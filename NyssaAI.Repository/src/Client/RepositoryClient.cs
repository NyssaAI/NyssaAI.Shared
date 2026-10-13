using System.Data.Common;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;
using NyssaAI.Results;

namespace NyssaAI.Repository.Client;

internal sealed class RepositoryClient : IRepository
{
    private readonly IRepositoryProvider _provider;
    private readonly ICommands _commands;

    internal RepositoryClient(IRepositoryProvider provider, ICommands commands)
    {
        _provider = provider;
        _commands = commands;
    }

    public Result<IRepoCommand<TParameters, TResult>> CommandRegistry<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters) =>
        _commands.Resolve(key, parameters);

    public async Task<Result<IRepositorySession>> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return opened.IsFailure ? Result<IRepositorySession>.Failure(opened.Errors) :
            Result<IRepositorySession>.Success(new RepositorySession(opened.Value, _provider.DatabaseProvider));
    }

    public async Task<Result<IRepositoryTransaction>> OpenTransactionAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure) return Result<IRepositoryTransaction>.Failure(opened.Errors);
        var didTransferOwnership = false;
        try
        {
            var transaction = await opened.Value.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var scope = new RepositoryTransaction(new RepositorySession(opened.Value, _provider.DatabaseProvider, transaction));
            didTransferOwnership = true;
            return Result<IRepositoryTransaction>.Success(scope);
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            return Result<IRepositoryTransaction>.Failure(RepositorySession.Translate(exception).Errors);
        }
        finally
        {
            if (!didTransferOwnership) await opened.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
