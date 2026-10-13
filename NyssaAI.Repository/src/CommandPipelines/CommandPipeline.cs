using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;
using NyssaAI.Results;

namespace NyssaAI.Repository.CommandPipelines;

/// <summary>CommandPipeline describes ordered typed commands on one caller-owned session or transaction.</summary>
/// <typeparam name="TResult">The complete successful result of the final command.</typeparam>
/// <remarks>
/// Construction performs no resolution or I/O. Every ExecuteAsync call reruns all steps, so avoid
/// repeating a pipeline with non-idempotent writes unintentionally. Execute sequentially on the scope.
/// The pipeline never opens, commits, rolls back, or disposes its scope. Transactional failures prevent
/// commit; a session-only pipeline does not make its commands atomic. Publish outbox messages separately.
/// </remarks>
public sealed class CommandPipeline<TResult> : ICommandPipelines<TResult>
{
    private readonly IRepositorySession _session;
    private readonly IRepository _repository;
    private readonly Func<CancellationToken, Task<Result<TResult>>> _execute;

    internal CommandPipeline(IRepositorySession session, IRepository repository,
        Func<CancellationToken, Task<Result<TResult>>> execute)
    {
        _session = session;
        _repository = repository;
        _execute = execute;
    }

    /// <summary>Adds a command whose parameters depend on the preceding successful result.</summary>
    /// <typeparam name="TNextParameters">The next command's parameter contract.</typeparam>
    /// <typeparam name="TNextResult">The next command's complete successful result.</typeparam>
    /// <param name="key">The next typed operation.</param>
    /// <param name="parameters">Creates values only after preceding steps succeed.</param>
    /// <returns>A deferred pipeline returning the next command's complete result.</returns>
    public CommandPipeline<TNextResult> Then<TNextParameters, TNextResult>(
        RepoCommandKey<TNextParameters, TNextResult> key, Func<TResult, TNextParameters>? parameters)
    {
        Func<TResult, Result<TNextParameters>>? validatedParameters = parameters is null
            ? null : value => Result<TNextParameters>.Success(parameters(value));
        return Then(key, validatedParameters);
    }

    /// <summary>Adds a dependent command with parameter validation that can stop the pipeline.</summary>
    /// <typeparam name="TNextParameters">The next command's parameter contract.</typeparam>
    /// <typeparam name="TNextResult">The next command's complete successful result.</typeparam>
    /// <param name="key">The next typed operation.</param>
    /// <param name="parameters">Returns typed values or a failure without running subsequent commands.</param>
    /// <returns>A deferred pipeline; parameter, resolution, execution, and cancellation failures stop dependent work.</returns>
    public CommandPipeline<TNextResult> Then<TNextParameters, TNextResult>(
        RepoCommandKey<TNextParameters, TNextResult> key, Func<TResult, Result<TNextParameters>>? parameters)
    {
        ICommandPipelines<TResult> preceding = this;
        return new CommandPipeline<TNextResult>(_session, _repository, async cancellationToken =>
        {
            if (parameters is null)
                return Result<TNextResult>.Failure(Error.FromMessage("A dependent parameter factory is required.", ErrorCategory.Validation));
            var previous = await preceding.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (previous.IsFailure) return Result<TNextResult>.Failure(previous.Errors);
            var ready = CheckReady(_session, _repository, cancellationToken);
            if (ready.IsFailure) return Result<TNextResult>.Failure(ready.Errors);
            var next = parameters(previous.Value);
            if (next is null)
                return Result<TNextResult>.Failure(Error.FromMessage("The parameter factory returned no outcome.", ErrorCategory.Validation));
            if (next.IsFailure) return Result<TNextResult>.Failure(next.Errors);
            return await CommandPipeline<TNextResult>.ResolveAndExecuteAsync(
                _session, _repository, key, next.Value, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>Runs every step sequentially and returns the final result or the first failure.</summary>
    /// <param name="cancellationToken">Stops dependent work and is forwarded to each command.</param>
    /// <returns>The final command result, or a scope, parameter, resolution, execution, or cancellation failure.</returns>
    /// <remarks>
    /// Command hooks execute only through the scope. Unexpected author exceptions leave a transaction
    /// uncommittable; cancellation from a parameter factory becomes a Canceled failure.
    /// </remarks>
    public async Task<Result<TResult>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var didFinish = false;
        try
        {
            var ready = CheckReady(_session, _repository, cancellationToken);
            var result = ready.IsFailure ? Result<TResult>.Failure(ready.Errors)
                : await _execute(cancellationToken).ConfigureAwait(false);
            if (result is null)
                result = Result<TResult>.Failure(Error.FromMessage("The command pipeline returned no outcome.", ErrorCategory.Unexpected));
            if (result.IsFailure) RecordFailure(Result.Failure(result.Errors));
            didFinish = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            var failure = Result.Failure(Error.FromMessage("Repository execution was canceled.", ErrorCategory.Canceled));
            RecordFailure(failure);
            didFinish = true;
            return Result<TResult>.Failure(failure.Errors);
        }
        finally
        {
            // A factory defect after an earlier write must not leave that transaction committable.
            if (!didFinish)
                RecordFailure(Result.Failure(Error.FromMessage("Command pipeline execution did not complete successfully.", ErrorCategory.Unexpected)));
        }
    }

    internal static async Task<Result<TResult>> ResolveAndExecuteAsync<TParameters>(IRepositorySession session,
        IRepository repository, RepoCommandKey<TParameters, TResult> key, TParameters parameters, CancellationToken cancellationToken)
    {
        var ready = CheckReady(session, repository, cancellationToken);
        if (ready.IsFailure) return Result<TResult>.Failure(ready.Errors);
        var command = repository.CommandRegistry(key, parameters);
        if (command.IsFailure) return Result<TResult>.Failure(command.Errors);
        return await session.ExecuteAsync(command.Value, cancellationToken).ConfigureAwait(false);
    }

    private static Result CheckReady(IRepositorySession session, IRepository repository, CancellationToken cancellationToken)
    {
        if (session is null || repository is null)
            return Result.Failure(Error.FromMessage("A repository and caller-owned scope are required.", ErrorCategory.Validation));
        if (session is IRepositoryScopeFailure scope) return scope.CheckReady(cancellationToken);
        return cancellationToken.IsCancellationRequested
            ? Result.Failure(Error.FromMessage("Repository execution was canceled.", ErrorCategory.Canceled))
            : Result.Success();
    }

    private void RecordFailure(Result failure)
    {
        if (_session is IRepositoryScopeFailure scope) scope.RecordFailure(failure);
    }
}
