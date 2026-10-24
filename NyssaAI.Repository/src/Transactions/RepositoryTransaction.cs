using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;

namespace NyssaAI.Repository.Transactions;

internal sealed class RepositoryTransaction : IRepositoryTransaction, IRepositoryScopeFailure
{
    private readonly RepositorySession _session;

    internal RepositoryTransaction(RepositorySession session) => _session = session;

    public Task<TResult> ExecuteAsync<TParameters, TResult>(IRepoCommand<TParameters, TResult> command, CancellationToken cancellationToken = default) =>
        _session.ExecuteAsync(command, cancellationToken);
    public Task<TResult> ExecuteAsync<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters, CancellationToken cancellationToken = default) =>
        _session.ExecuteAsync(key, parameters, cancellationToken);
    public Task CommitAsync(CancellationToken cancellationToken = default) => _session.CompleteAsync(true, cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken = default) => _session.CompleteAsync(false, cancellationToken);
    public ValueTask DisposeAsync() => _session.DisposeAsync();
    public void RecordFailure(Exception failure) => _session.RecordFailure(failure);
    public void CheckReady(CancellationToken cancellationToken) => ((IRepositoryScopeFailure)_session).CheckReady(cancellationToken);
}
