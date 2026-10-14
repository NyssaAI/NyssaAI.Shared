
namespace NyssaAI.Repository.Commands;

internal interface ICommands
{
    public IRepoCommand<TParameters, TResult> Resolve<TParameters, TResult>(RepoCommandKey<TParameters, TResult> key, TParameters parameters);
}
