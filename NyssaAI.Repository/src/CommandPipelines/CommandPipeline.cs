using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;

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
    private readonly IRepository? _repository;
    private readonly bool _requireRepository;
    private readonly Func<CancellationToken, Task<TResult>> _execute;

    internal CommandPipeline(IRepositorySession session, IRepository? repository, bool requireRepository, Func<CancellationToken, Task<TResult>> execute)
    {
        _session = session;
        _repository = repository;
        _requireRepository = requireRepository;
        _execute = execute;
    }

    /// <summary>Adds a command whose parameters depend on the preceding successful result.</summary>
    /// <typeparam name="TNextParameters">The next command's input contract.</typeparam>
    /// <typeparam name="TNextResult">The next command's complete result.</typeparam>
    /// <param name="key">The next logical operation.</param>
    /// <param name="parameters">Creates its input, or throws to stop execution.</param>
    /// <returns>A deferred pipeline with the next command's result type.</returns>
    public CommandPipeline<TNextResult> Then<TNextParameters, TNextResult>(
        RepoCommandKey<TNextParameters, TNextResult> key, Func<TResult, TNextParameters>? parameters) =>
        new(_session, _repository, _requireRepository, async cancellationToken =>
        {
            ArgumentNullException.ThrowIfNull(parameters);
            var previous = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
            CheckReady(_session, _repository, _requireRepository, cancellationToken);
            var next = parameters!(previous);
            return await CommandPipeline<TNextResult>.ResolveAndExecuteAsync(
                _session, _repository, _requireRepository, key, next, cancellationToken).ConfigureAwait(false);
        });

    /// <summary>Runs each step sequentially and returns the final value. Any failure throws and stops dependent work.</summary>
    /// <param name="cancellationToken">Cancels execution and dependent work.</param>
    /// <returns>The final command's value.</returns>
    /// <remarks>A failed pipeline leaves its transaction unable to commit, even if the caller catches the exception.</remarks>
    public async Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CheckReady(_session, _repository, _requireRepository, cancellationToken);
            return await _execute(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    internal static async Task<TResult> ResolveAndExecuteAsync<TParameters>(IRepositorySession session,
        IRepository? repository, bool requireRepository, RepoCommandKey<TParameters, TResult> key, TParameters parameters, CancellationToken cancellationToken)
    {
        CheckReady(session, repository, requireRepository, cancellationToken);
        if (!requireRepository)
            return await session.ExecuteAsync(key, parameters, cancellationToken).ConfigureAwait(false);
        var command = repository!.CommandRegistry(key, parameters);
        return await session.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static void CheckReady(IRepositorySession session, IRepository? repository, bool requireRepository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (requireRepository) ArgumentNullException.ThrowIfNull(repository);
        if (session is IRepositoryScopeFailure scope) scope.CheckReady(cancellationToken);
        else cancellationToken.ThrowIfCancellationRequested();
    }

    private void RecordFailure(Exception failure)
    {
        if (_session is IRepositoryScopeFailure scope) scope.RecordFailure(failure);
    }
}
