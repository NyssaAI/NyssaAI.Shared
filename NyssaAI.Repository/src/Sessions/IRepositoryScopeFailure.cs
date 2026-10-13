using NyssaAI.Results;

namespace NyssaAI.Repository.Sessions;

internal interface IRepositoryScopeFailure
{
    public Result CheckReady(CancellationToken cancellationToken);
    public void RecordFailure(Result failure);
}
