
namespace NyssaAI.Repository.CommandPipelines;

internal interface ICommandPipelines<TResult>
{
    public Task<TResult> ExecuteAsync(CancellationToken cancellationToken);
}
