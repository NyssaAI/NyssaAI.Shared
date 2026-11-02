using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// Prepares, executes, and reconciles individual recovery steps against local and cloud key vault providers.
/// </summary>
internal sealed class ConfigurationRecoveryStepRunner
{
    public static Error RecoveryUnfinished => Error.FromMessage(
        "The configuration operation is unfinished.", ErrorCategory.Unavailable);

    public static Error RecoveryConflict => Error.FromMessage(
        "Configuration changed after this operation. Recovery stopped.", ErrorCategory.Conflict);

    public static Error RecoveryManual => Error.FromMessage(
        "The provider cannot establish safe recovery. Reconcile the recorded operation manually.", ErrorCategory.Indeterminate);

    private readonly Func<string, Result<IConfigurationProvider>> _getProvider;

    public ConfigurationRecoveryStepRunner(Func<string, Result<IConfigurationProvider>> getProvider)
    {
        _getProvider = getProvider;
    }

    public static bool IsTerminal(ConfigurationRecoveryStep step) =>
        step.State is ConfigurationStepState.Applied or ConfigurationStepState.Retained;

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
                if (reconcile && IsTerminal(step) && effect.State == ConfigurationStepState.Failed)
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

    public async ValueTask<Result> WriteStepsAsync(ConfigurationRecoveryState state, CancellationToken token)
    {
        for (var index = 0; index < state.Steps.Count; index++)
        {
            var step = state.Steps[index];
            if (step.Operation.Action != CloudKeyVaultAction.Write) continue;
            if (!IsTerminal(step))
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
            if (!IsTerminal(step))
            {
                var cleaned = await RunStepAsync(state, index, false, token).ConfigureAwait(false);
                if (cleaned.IsFailure) return cleaned;
            }
        }
        return Result.Success();
    }
}
