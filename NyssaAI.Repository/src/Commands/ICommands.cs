using NyssaAI.Results;

namespace NyssaAI.Repository.Commands;

internal interface ICommands
{
    public Result<IRepoCommand<TParameters, TResult>> Resolve<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters);
}
