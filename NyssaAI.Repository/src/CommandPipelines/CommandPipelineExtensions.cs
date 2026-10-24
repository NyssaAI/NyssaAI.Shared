using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;

namespace NyssaAI.Repository.CommandPipelines;

/// <summary>CommandPipelineExtensions starts deferred typed command pipelines on a caller-owned scope.</summary>
public static class CommandPipelineExtensions
{
    /// <summary>Describes the first command on a scope without resolving it or performing database work.</summary>
    /// <typeparam name="TParameters">The first command's parameter contract.</typeparam>
    /// <typeparam name="TResult">The first command's complete successful result.</typeparam>
    /// <param name="session">The caller-owned session or transaction used by every step.</param>
    /// <param name="key">The first typed operation.</param>
    /// <param name="parameters">The values supplied when execution starts.</param>
    /// <returns>A deferred pipeline; invalid arguments throw during execution.</returns>
    public static CommandPipeline<TResult> Pipeline<TParameters, TResult>(this IRepositorySession session,
        RepoCommandKey<TParameters, TResult> key, TParameters parameters) =>
        new(session, null, false, cancellationToken =>
            CommandPipeline<TResult>.ResolveAndExecuteAsync(session, null, false, key, parameters, cancellationToken));

    /// <summary>Describes the first command without resolving it or performing database work.</summary>
    /// <typeparam name="TParameters">The first command's parameter contract.</typeparam>
    /// <typeparam name="TResult">The first command's complete successful result.</typeparam>
    /// <param name="session">The caller-owned session or transaction used by every step.</param>
    /// <param name="repository">The repository selecting commands for the scope's provider.</param>
    /// <param name="key">The first typed operation.</param>
    /// <param name="parameters">The values supplied when execution starts.</param>
    /// <returns>A deferred pipeline; invalid arguments throw during execution.</returns>
    public static CommandPipeline<TResult> Pipeline<TParameters, TResult>(this IRepositorySession session,
        IRepository repository, RepoCommandKey<TParameters, TResult> key, TParameters parameters) =>
        new(session, repository, true, cancellationToken =>
            CommandPipeline<TResult>.ResolveAndExecuteAsync(session, repository, true, key, parameters, cancellationToken));
}
