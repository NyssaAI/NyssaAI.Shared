using System.Text.Json;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ConfigurationRecoveryCoordinator executes two-phase multi-step operations, recovery receipts,
/// and compensating actions across local documents and cloud key vaults.
/// </summary>
internal sealed class ConfigurationRecoveryCoordinator
{
    private static Error RecoveryUnfinished => Error.FromMessage(
        "The configuration operation is unfinished.", ErrorCategory.Unavailable);

    private static Error RecoveryConflict => Error.FromMessage(
        "Configuration changed after this operation. Recovery stopped.", ErrorCategory.Conflict);

    private static Error RecoveryManual => Error.FromMessage(
        "The provider cannot establish safe recovery. Reconcile the recorded operation manually.", ErrorCategory.Indeterminate);

    private static Error RecoveryAlreadyPublished => Error.FromMessage(
        "Publication already completed.", ErrorCategory.Conflict);

    private readonly IConfigurationDocumentFactory _documents;
    private readonly Func<string, Result<IConfigurationProvider>> _getProvider;
    private readonly IRecovery? _recovery;

    public ConfigurationRecoveryCoordinator(
        IConfigurationDocumentFactory documents,
        Func<string, Result<IConfigurationProvider>> getProvider,
        IRecovery? recovery)
    {
        _documents = documents;
        _getProvider = getProvider;
        _recovery = recovery;
    }

    public async ValueTask<Result<ConfigurationRecoveryStep>> PrepareStepAsync(
        string providerName, CloudKeyVaultAction action, string ns, string key, string? value,
        ConfigurationValue? source, bool json, CancellationToken token)
    {
        IConfigurationProvider? provider = null;
        if (providerName.Length > 0)
        {
            var found = _getProvider(providerName);
            if (found.IsFailure) return Result<ConfigurationRecoveryStep>.Failure(found.Errors);
            provider = found.Value;
        }
        ConfigurationPointer? pointer = null;
        if (provider?.Recovery is not null && source is not null)
        {
            var parsed = ConfigurationPointer.Parse(providerName, source.Value);
            if (parsed.IsFailure) return Result<ConfigurationRecoveryStep>.Failure(parsed.Errors);
            pointer = parsed.Value;
        }
        var operation = new CloudKeyVaultOperation
        {
            OperationId = Guid.NewGuid(), Action = action, Namespace = ns, Key = key, Value = value, Pointer = pointer,
        };
        CloudKeyVaultPreparation? preparation = null;
        if (provider?.Recovery is { } capability)
        {
            var prepared = await capability.PrepareAsync(operation, token).ConfigureAwait(false);
            if (prepared.IsFailure) return Result<ConfigurationRecoveryStep>.Failure(prepared.Errors);
            if (string.IsNullOrWhiteSpace(prepared.Value.StoreIdentity)
                || prepared.Value.StoreIdentity != capability.StoreIdentity)
                return Result<ConfigurationRecoveryStep>.Failure(RecoveryConflict);
            preparation = prepared.Value;
        }
        return Result<ConfigurationRecoveryStep>.Success(new ConfigurationRecoveryStep
        {
            Provider = providerName, Operation = operation, Local = provider is null || provider.IsLocalOnly,
            Source = source, IsJsonValue = json, Preparation = preparation,
            TargetReference = preparation?.TargetReference ?? (source is null ? null : provider?.TargetReference(source)),
        });
    }

    public async ValueTask<Result> RunStepAsync(ConfigurationRecoveryState state, int index, bool reconcile, CancellationToken token)
    {
        var step = state.Steps[index];
        var request = step.Operation;
        token.ThrowIfCancellationRequested();
        IConfigurationProvider? provider = null;
        if (step.Provider.Length > 0)
        {
            var found = _getProvider(step.Provider);
            if (found.IsFailure) return Result.Failure(found.Errors);
            provider = found.Value;
        }
        if (step.Preparation is { } preparation)
        {
            var capability = provider?.Recovery;
            if (capability is null || capability.StoreIdentity != preparation.StoreIdentity)
            {
                state.Steps[index] = step with { State = ConfigurationStepState.Conflict };
                return Result.Failure(RecoveryConflict);
            }
            state.Steps[index] = step with { State = ConfigurationStepState.Unknown };
            try
            {
                var result = reconcile
                    ? await capability.ReconcileAsync(request, preparation, token).ConfigureAwait(false)
                    : await capability.ExecuteAsync(request, preparation, token).ConfigureAwait(false);
                if (result.IsFailure) return Result.Failure(result.Errors);
                var effect = result.Value;
                if (reconcile && Terminal(step) && effect.State == ConfigurationStepState.Failed)
                {
                    state.Steps[index] = step with { State = ConfigurationStepState.Manual };
                    return Result.Failure(RecoveryManual);
                }
                if (effect.State == ConfigurationStepState.Pending
                    || (effect.State == ConfigurationStepState.Retained && request.Action != CloudKeyVaultAction.Remove))
                    return Result.Failure(RecoveryUnfinished);
                ConfigurationValue? output = step.Output;
                if (effect.State == ConfigurationStepState.Applied && request.Action == CloudKeyVaultAction.Write)
                {
                    if (effect.Pointer is null || effect.Pointer.Provider != step.Provider
                        || ConfigurationPointer.Parse(effect.Pointer.Provider, effect.Pointer.Value).IsFailure)
                        return Result.Failure(RecoveryUnfinished);
                    if (output is not null && output.Value != effect.Pointer.Value)
                    {
                        state.Steps[index] = step with { State = ConfigurationStepState.Conflict };
                        return Result.Failure(RecoveryConflict);
                    }
                    output = new ConfigurationValue
                    {
                        Provider = step.Provider, Value = effect.Pointer.Value, IsJsonValue = step.IsJsonValue,
                    };
                }
                state.Steps[index] = step with
                {
                    State = effect.State, Output = output,
                };
                if (effect.State is ConfigurationStepState.Applied or ConfigurationStepState.Retained
                    || (reconcile && effect.State == ConfigurationStepState.Failed))
                    return Result.Success();
                return Result.Failure(effect.State == ConfigurationStepState.Conflict ? RecoveryConflict
                    : effect.State == ConfigurationStepState.Manual ? RecoveryManual : RecoveryUnfinished);
            }
            catch (OperationCanceledException)
            {
                return Result.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
            }
            catch (Exception)
            {
                return Result.Failure(RecoveryUnfinished);
            }
        }
        if (reconcile && !step.Local)
        {
            state.Steps[index] = step with { State = ConfigurationStepState.Manual };
            return Result.Failure(RecoveryManual);
        }
        state.Steps[index] = step with { State = step.Local ? ConfigurationStepState.Failed : ConfigurationStepState.Unknown };
        try
        {
            if (request.Action == CloudKeyVaultAction.Write)
            {
                var result = provider is null
                    ? Result<ConfigurationValue>.Success(new ConfigurationValue { Value = request.Value!, IsJsonValue = step.IsJsonValue })
                    : await provider.WriteAsync(request.Value!, state.Namespace, request.Key, token).ConfigureAwait(false);
                if (result.IsFailure) return Result.Failure(result.Errors);
                state.Steps[index] = step with
                {
                    State = ConfigurationStepState.Applied,
                    Output = result.Value with { IsJsonValue = step.IsJsonValue },
                };
            }
            else
            {
                var result = provider is null
                    ? Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false })
                    : await provider.EraseAsync(step.Source!, state.Namespace, request.Key, token).ConfigureAwait(false);
                if (result.IsFailure) return Result.Failure(result.Errors);
                state.Steps[index] = step with { State = result.Value.RemoteValueRemains ? ConfigurationStepState.Retained : ConfigurationStepState.Applied };
            }
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (Exception) { return Result.Failure(RecoveryUnfinished); }
    }

    public static Dictionary<string, ConfigurationValue> Snapshot(IDictionary<string, ConfigurationValue> entries) =>
        new(entries, StringComparer.Ordinal);

    private static bool Matches(IDictionary<string, ConfigurationValue> entries, Dictionary<string, ConfigurationValue> expected) =>
        entries.Count == expected.Count && expected.All(pair => entries.TryGetValue(pair.Key, out var value) && MatchesValue(value, pair.Value));

    private static bool MatchesValue(ConfigurationValue actual, ConfigurationValue expected)
    {
        if (actual == expected) return true;
        if (!actual.IsLiteral || !actual.IsJsonValue || actual.Encrypted
            || actual with { Value = expected.Value } != expected)
            return false;

        using var actualJson = JsonDocument.Parse(actual.Value);
        using var expectedJson = JsonDocument.Parse(expected.Value);
        return JsonElement.DeepEquals(actualJson.RootElement, expectedJson.RootElement);
    }

    private static bool MatchesCompletedProposal(IDictionary<string, ConfigurationValue> entries, ConfigurationRecoveryState state) =>
        Matches(entries, state.Proposed) && state.Steps.Where(step => step.Operation.Action == CloudKeyVaultAction.Write).All(Terminal);

    private static bool Terminal(ConfigurationRecoveryStep step) =>
        step.State is ConfigurationStepState.Applied or ConfigurationStepState.Retained;

    private static IEnumerable<ConfigurationRecoveryStep> RelevantSteps(ConfigurationRecoveryState state) =>
        state.Publication == ConfigurationPublicationState.Published
            ? state.Steps.Where(step => step.Operation.Action == CloudKeyVaultAction.Remove)
            : state.Steps;

    private static ConfigurationRecoveryStatus Status(ConfigurationRecoveryState state)
    {
        if (state.Conflicted) return ConfigurationRecoveryStatus.Conflict;
        var relevant = RelevantSteps(state).ToArray();
        if (relevant.Any(step => step.State == ConfigurationStepState.Conflict)) return ConfigurationRecoveryStatus.Conflict;
        if (relevant.Any(step => step.State == ConfigurationStepState.Manual
            || (!step.Local && step.Preparation is null && !Terminal(step))))
            return ConfigurationRecoveryStatus.ManualReconciliation;
        if (state.Publication == ConfigurationPublicationState.Published && relevant.All(Terminal))
            return ConfigurationRecoveryStatus.Completed;
        if (relevant.Any(step => !step.Local && step.Preparation is null))
            return ConfigurationRecoveryStatus.ManualReconciliation;
        return ConfigurationRecoveryStatus.Unfinished;
    }

    private static ConfigurationOperationProgress Progress(ConfigurationRecoveryState state, ConfigurationRecoveryStatus status) => new()
    {
        OperationId = state.OperationId, Namespace = state.Namespace, Kind = state.Kind,
        Publication = state.Publication, Recovery = status,
        Steps = Array.AsReadOnly(state.Steps.Select(step => new ConfigurationStepProgress
        {
            OperationId = step.Operation.OperationId, Key = step.Operation.Key, Provider = step.Provider,
            Action = step.Operation.Action, State = step.State, TargetReference = step.TargetReference,
        }).ToArray()),
    };

    private ConfigurationRecoveryHandle? Handle(ConfigurationRecoveryState state, ConfigurationRecoveryStatus status)
    {
        if (_recovery is null || status != ConfigurationRecoveryStatus.Unfinished) return null;
        var sealedState = _recovery.Seal(state);
        return sealedState.IsSuccess ? sealedState.Value : null;
    }

    public ConfigurationRecoveryOutcome Outcome(ConfigurationRecoveryState state)
    {
        var status = Status(state);
        return new ConfigurationRecoveryOutcome { Status = status, Progress = Progress(state, status), RecoveryHandle = Handle(state, status) };
    }

    public ConfigurationAttempt Attempt(Result result, ConfigurationRecoveryState? state)
    {
        var outcome = state is null ? null : Outcome(state);
        return new ConfigurationAttempt { Result = result, Progress = outcome?.Progress, RecoveryHandle = outcome?.RecoveryHandle };
    }

    public async ValueTask<Result> WriteStepsAsync(ConfigurationRecoveryState state, CancellationToken token)
    {
        for (var index = 0; index < state.Steps.Count; index++)
        {
            var step = state.Steps[index];
            if (step.Operation.Action != CloudKeyVaultAction.Write) continue;
            if (!Terminal(step))
            {
                var written = await RunStepAsync(state, index, false, token).ConfigureAwait(false);
                if (written.IsFailure) return written;
            }
            state.Proposed[step.Operation.Key] = state.Steps[index].Output!;
        }
        return Result.Success();
    }

    public async ValueTask<Result> CleanupStepsAsync(ConfigurationRecoveryState state, CancellationToken token)
    {
        for (var index = 0; index < state.Steps.Count; index++)
        {
            var step = state.Steps[index];
            if (step.Operation.Action != CloudKeyVaultAction.Remove) continue;
            if (!Terminal(step))
            {
                var cleaned = await RunStepAsync(state, index, false, token).ConfigureAwait(false);
                if (cleaned.IsFailure) return cleaned;
            }
        }
        return Result.Success();
    }

    public async ValueTask<ConfigurationAttempt<ConfigurationRecoveryOutcome>> RecoverAsync(
        ConfigurationRecoveryHandle handle, CancellationToken cancellationToken = default)
    {
        if (_recovery is null)
            return Result<ConfigurationRecoveryOutcome>.Failure(
                Error.FromMessage("Recovery is not configured.", ErrorCategory.Unavailable));
        var opened = _recovery.Open(handle);
        if (opened.IsFailure)
            return Result<ConfigurationRecoveryOutcome>.Failure(opened.Errors);
        var state = opened.Value;
        var document = _documents.Create(state.Namespace);
        if (document.IsFailure)
            return Result<ConfigurationRecoveryOutcome>.Failure(document.Errors);

        var publicationStarted = false;
        Result result;
        try
        {
            result = await document.Value.ModifyAsync(async entries =>
            {
                var original = Matches(entries, state.Original);
                var proposed = MatchesCompletedProposal(entries, state);
                if (!original && !proposed)
                {
                    state = MarkConflict(state);
                    return Result.Failure(RecoveryConflict);
                }
                for (var index = 0; index < state.Steps.Count; index++)
                {
                    var step = state.Steps[index];
                    if (step.Local || step.State == ConfigurationStepState.Retained) continue;
                    if (state.Publication == ConfigurationPublicationState.Published && step.Operation.Action == CloudKeyVaultAction.Write) continue;
                    var reconciled = await RunStepAsync(state, index, true, cancellationToken).ConfigureAwait(false);
                    if (reconciled.IsFailure) return reconciled;
                }
                foreach (var step in state.Steps.Where(step => step.Operation.Action == CloudKeyVaultAction.Write && step.Output is not null))
                    state.Proposed[step.Operation.Key] = step.Output!;
                proposed = MatchesCompletedProposal(entries, state);
                if (!original && !proposed)
                {
                    state = MarkConflict(state);
                    return Result.Failure(RecoveryConflict);
                }
                if (proposed) state = state with { Publication = ConfigurationPublicationState.Published };
                if (state.Publication != ConfigurationPublicationState.Published)
                {
                    var written = await WriteStepsAsync(state, cancellationToken).ConfigureAwait(false);
                    if (written.IsFailure) return written;
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Clear();
                    foreach (var entry in state.Proposed) entries.Add(entry.Key, entry.Value);
                    publicationStarted = true;
                    state = state with { Publication = ConfigurationPublicationState.Unknown };
                    return Result.Success();
                }
                var cleanup = await CleanupStepsAsync(state, cancellationToken).ConfigureAwait(false);
                return cleanup.IsFailure ? cleanup : Result.Failure(RecoveryAlreadyPublished);
            }, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                state = state with { Publication = ConfigurationPublicationState.Published };
                result = await CleanupStepsAsync(state, cancellationToken).ConfigureAwait(false);
            }
            else if (publicationStarted)
                state = state with { Publication = ConfigurationPublicationState.Failed };
        }
        catch (OperationCanceledException)
        {
            result = Result.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (Exception) { result = Result.Failure(RecoveryUnfinished); }

        var outcome = Outcome(state);
        if (result.Errors.Any(error => error.Category == ErrorCategory.Canceled))
            return new ConfigurationAttempt<ConfigurationRecoveryOutcome>
            {
                Result = Result<ConfigurationRecoveryOutcome>.Failure(result.Errors), Progress = outcome.Progress, RecoveryHandle = outcome.RecoveryHandle,
            };
        return Result<ConfigurationRecoveryOutcome>.Success(outcome);
    }

    private static ConfigurationRecoveryState MarkConflict(ConfigurationRecoveryState state)
    {
        if (state.Steps.Count > 0)
            state.Steps[0] = state.Steps[0] with { State = ConfigurationStepState.Conflict };
        return state with { Conflicted = true };
    }
}
