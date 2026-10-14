
namespace NyssaAI.Repository.Sessions;

internal interface IRepositoryScopeFailure
{
    public void CheckReady(CancellationToken cancellationToken);
    public void RecordFailure(Exception failure);
}
