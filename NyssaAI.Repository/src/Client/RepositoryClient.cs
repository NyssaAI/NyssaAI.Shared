using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;

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

    public IRepoCommand<TParameters, TResult> CommandRegistry<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters) =>
        _commands.Resolve(key, parameters);

    public async Task<IRepositorySession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return new RepositorySession(opened, _provider.DatabaseProvider, commands: _commands);
    }

    public async Task<IRepositoryTransaction> OpenTransactionAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _provider.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var didTransferOwnership = false;
        try
        {
            var transaction = await opened.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var scope = new RepositoryTransaction(new RepositorySession(opened, _provider.DatabaseProvider, transaction, _commands));
            didTransferOwnership = true;
            return scope;
        }
        finally
        {
            if (!didTransferOwnership) await opened.DisposeAsync().ConfigureAwait(false);
        }
    }
}
