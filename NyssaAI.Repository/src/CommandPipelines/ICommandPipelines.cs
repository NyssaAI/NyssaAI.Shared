using NyssaAI.Results;

namespace NyssaAI.Repository.CommandPipelines;

internal interface ICommandPipelines<TResult>
{
    public Task<Result<TResult>> ExecuteAsync(CancellationToken cancellationToken);
}
