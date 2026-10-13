using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;
using NyssaAI.Results;

namespace NyssaAI.Repository.Transactions;

internal sealed class RepositoryTransaction : IRepositoryTransaction, IRepositoryScopeFailure
{
    private readonly RepositorySession _session;

    internal RepositoryTransaction(RepositorySession session) => _session = session;

    public Task<Result<TResult>> ExecuteAsync<TParameters, TResult>(IRepoCommand<TParameters, TResult> command, CancellationToken cancellationToken = default) =>
        _session.ExecuteAsync(command, cancellationToken);
    public Task<Result> CommitAsync(CancellationToken cancellationToken = default) => _session.CompleteAsync(true, cancellationToken);
    public Task<Result> RollbackAsync(CancellationToken cancellationToken = default) => _session.CompleteAsync(false, cancellationToken);
    public ValueTask DisposeAsync() => _session.DisposeAsync();
    public void RecordFailure(Result failure) => _session.RecordFailure(failure);
    public Result CheckReady(CancellationToken cancellationToken) => ((IRepositoryScopeFailure)_session).CheckReady(cancellationToken);
}
