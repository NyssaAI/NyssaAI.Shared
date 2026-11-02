using System.Text.Json;
using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// Executes single-entry read, write, move, removal, and key-listing operations against namespace documents.
/// </summary>
internal sealed class ConfigurationEntryHandler
{
    private static Error RemovalEntryChanged(string @namespace, string key) => Error.FromMessages(
        "The configuration entry changed before it could be removed. Try again.",
        $"Configuration entry '{@namespace}/{key}' changed while preparing removal.",
        ErrorCategory.Conflict);

    private readonly IConfigurationDocumentFactory _documents;
    private readonly ConfigurationProviderRegistry _registry;
    private readonly ConfigurationRecoveryCoordinator _recoveryCoordinator;
    private readonly ILogger? _logger;

    public ConfigurationEntryHandler(
        IConfigurationDocumentFactory documents,
        ConfigurationProviderRegistry registry,
        ConfigurationRecoveryCoordinator recoveryCoordinator,
        ILogger? logger)
    {
        _documents = documents;
        _registry = registry;
        _recoveryCoordinator = recoveryCoordinator;
        _logger = logger;
    }

    public async ValueTask<Result<string>> ReadValueAsync(
        string @namespace, string key, bool encodeString, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.IsFailure)
            return Result<string>.Failure(entries.Errors);

        if (!entries.Value.TryGetValue(key, out var entry))
            return Result<string>.Failure(ConfigurationStore.EntryNotFound(@namespace, key));

        var validation = entry.Validate();
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Errors);

        // The entry names the provider that wrote it, so a read never has to be told how the value
        // was stored. An entry naming nothing is a literal and resolves to itself.
        Result<string> resolved;
        if (entry.IsLiteral)
            resolved = Result<string>.Success(entry.Value);
        else
        {
            var provider = _registry.GetProvider(entry.Provider);
            if (provider.IsFailure)
                return Result<string>.Failure(provider.Errors);

            resolved = await provider.Value.ReadAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                _logger?.LogWarning(
                    "Provider '{Provider}' could not resolve {Namespace}/{Key} ({Category})",
                    entry.Provider, @namespace, key, resolved.Errors[0].Category);
                return resolved;
            }
        }

        // Namespace properties and SetAsync store strings without JSON quotes. Typed string
        // reads encode that text before deserialization, preserving quotes and JSON-looking text
        // while still honoring the caller's converter. Already-encoded JSON is never reinterpreted.
        return encodeString && !entry.IsJsonValue
            ? Result<string>.Success(JsonSerializer.Serialize(resolved.Value, ConfigurationJsonContext.Default.String))
            : resolved;
    }

    public ValueTask<Result<string>> ReadThroughAsync(
        ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken)
    {
        var provider = _registry.GetProvider(entry.Provider);
        if (provider.IsFailure)
            return ValueTask.FromResult(Result<string>.Failure(provider.Errors));
        return provider.Value.ReadAsync(entry, @namespace, key, cancellationToken);
    }

    public async ValueTask<Result> SetValueAsync(
        string @namespace, string key, string value, string? provider,
        bool isJsonValue, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);

        return await document.Value.ModifyAsync(
            async entries =>
            {
                var exists = entries.TryGetValue(key, out var existing);

                // An overwrite inherits where the value already lives. That is what stops a write
                // from quietly turning a sealed secret into a plaintext literal, which is exactly
                // what happens when the destination is re-decided on every call.
                var destination = provider ?? existing?.Provider ?? string.Empty;

                if (exists && !string.Equals(destination, existing!.Provider, StringComparison.Ordinal))
                    return Result.Failure(ConfigurationStore.AlreadyStoredElsewhere(@namespace, key, existing.Provider));

                var circular = _registry.CheckCircular(destination, @namespace, key);
                if (circular.IsFailure)
                    return circular;

                if (destination.Length == 0)
                {
                    entries[key] = new ConfigurationValue { Value = value, IsJsonValue = isJsonValue };
                    return Result.Success();
                }

                var target = _registry.GetProvider(destination);
                if (target.IsFailure)
                    return Result.Failure(target.Errors);

                var written = await target.Value.WriteAsync(value, @namespace, key, cancellationToken).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    _logger?.LogWarning(
                        "Provider '{Provider}' could not store {Namespace}/{Key} ({Category})",
                        destination, @namespace, key, written.Errors[0].Category);
                    return Result.Failure(written.Errors);
                }

                entries[key] = written.Value with { IsJsonValue = isJsonValue };
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ConfigurationAttempt<ConfigurationMove>> MoveAsync(
        string @namespace, string key, string? toProvider, CancellationToken cancellationToken)
    {
        toProvider ??= string.Empty;
        var document = _documents.Create(@namespace);
        if (document.IsFailure) return Result<ConfigurationMove>.Failure(document.Errors);
        ConfigurationRecoveryState? state = null;
        var from = string.Empty;
        var publishing = false;
        Result result;
        try
        {
            result = await document.Value.ModifyAsync(async entries =>
            {
                if (!entries.TryGetValue(key, out var existing)) return Result.Failure(ConfigurationStore.EntryNotFound(@namespace, key));
                if (existing.Provider == toProvider) return Result.Failure(ConfigurationStore.AlreadyThere(@namespace, key, toProvider));
                var circular = _registry.CheckCircular(toProvider, @namespace, key);
                if (circular.IsFailure) return circular;
                // Capture the deletion condition before reading the value; a replacement between
                // preparation and reading cannot subsequently be deleted by this operation.
                var cleanup = await _recoveryCoordinator.PrepareStepAsync(existing.Provider, CloudKeyVaultAction.Remove,
                    @namespace, key, null, existing, existing.IsJsonValue, cancellationToken).ConfigureAwait(false);
                if (cleanup.IsFailure) return Result.Failure(cleanup.Errors);
                var plaintext = existing.IsLiteral ? Result<string>.Success(existing.Value)
                    : await ReadThroughAsync(existing, @namespace, key, cancellationToken).ConfigureAwait(false);
                if (plaintext.IsFailure) return Result.Failure(plaintext.Errors);
                var destination = await _recoveryCoordinator.PrepareStepAsync(toProvider, CloudKeyVaultAction.Write,
                    @namespace, key, plaintext.Value, null, existing.IsJsonValue, cancellationToken).ConfigureAwait(false);
                if (destination.IsFailure) return Result.Failure(destination.Errors);
                from = existing.Provider;
                state = new ConfigurationRecoveryState
                {
                    OperationId = Guid.NewGuid(), Namespace = @namespace, Kind = ConfigurationOperationKind.Move,
                    Original = ConfigurationRecoveryCoordinator.Snapshot(entries), Proposed = ConfigurationRecoveryCoordinator.Snapshot(entries), Steps = [destination.Value, cleanup.Value],
                };
                var written = await _recoveryCoordinator.WriteStepsAsync(state, cancellationToken).ConfigureAwait(false);
                if (written.IsFailure) return written;
                cancellationToken.ThrowIfCancellationRequested();
                entries[key] = state.Proposed[key];
                publishing = true;
                state = state with { Publication = ConfigurationPublicationState.Unknown };
                return Result.Success();
            }, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                state = state! with { Publication = ConfigurationPublicationState.Published };
                result = await _recoveryCoordinator.CleanupStepsAsync(state, cancellationToken).ConfigureAwait(false);
            }
            else if (publishing)
                state = state! with { Publication = ConfigurationPublicationState.Failed };
        }
        catch (OperationCanceledException)
        {
            result = Result.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (Exception) { result = Result.Failure(Error.FromMessage("The configuration operation is unfinished.", ErrorCategory.Unavailable)); }

        var outcome = state is null ? null : _recoveryCoordinator.Outcome(state);
        var progress = outcome?.Progress;
        var handle = outcome?.RecoveryHandle;
        if (state?.Publication != ConfigurationPublicationState.Published
            || result.Errors.Any(error => error.Category == ErrorCategory.Canceled))
            return new ConfigurationAttempt<ConfigurationMove>
            {
                Result = Result<ConfigurationMove>.Failure(result.Errors), Progress = progress, RecoveryHandle = handle,
            };
        var withdrawn = state.Steps.Last().State == ConfigurationStepState.Applied;
        if (!withdrawn)
            _logger?.LogWarning("Moved {Namespace}/{Key}; source cleanup for '{Provider}' is {State}",
                @namespace, key, from, state.Steps.Last().State);
        return Result<ConfigurationMove>.Success(new ConfigurationMove
        {
            Namespace = @namespace, Key = key, From = from, To = toProvider, SourceWithdrawn = withdrawn,
            Progress = progress, RecoveryHandle = handle,
        });
    }

    public async ValueTask<Result<ConfigurationRemoval>> RemoveAsync(
        string @namespace,
        string key,
        Func<string, CancellationToken, ValueTask<Result<string>>> resolveCredentialAsync,
        CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<ConfigurationRemoval>.Failure(document.Errors);

        var removal = new ConfigurationRemoval { RemoteValueRemains = false };
        try
        {
            var read = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (read.IsFailure)
                return Result<ConfigurationRemoval>.Failure(read.Errors);
            if (!read.Value.TryGetValue(key, out var expected))
                return Result<ConfigurationRemoval>.Success(removal);

            var circular = _registry.CheckCircular(expected.Provider, @namespace, key);
            if (circular.IsFailure)
                return Result<ConfigurationRemoval>.Failure(circular.Errors);

            // A colocated OAuth credential may need its own locked refresh and publication.
            // Finish that first, then keep remote deletion and pointer removal under exclusion.
            (string Provider, string Token)? preparedCredential = null;
            if (_registry.HasCredential(expected.Provider))
            {
                var credential = await resolveCredentialAsync(expected.Provider, cancellationToken).ConfigureAwait(false);
                if (credential.IsFailure)
                    return Result<ConfigurationRemoval>.Failure(credential.Errors);
                preparedCredential = (expected.Provider, credential.Value);
            }

            var modified = await document.Value.ModifyAsync(
                async entries =>
                {
                    if (!entries.TryGetValue(key, out var entry))
                        return Result.Success();
                    if (entry != expected)
                        return Result.Failure(RemovalEntryChanged(@namespace, key));

                    if (!entry.IsLiteral)
                    {
                        var provider = _registry.GetProvider(entry.Provider);
                        if (provider.IsFailure)
                            return Result.Failure(provider.Errors);

                        cancellationToken.ThrowIfCancellationRequested();
                        var erased = await _registry.EraseWithPreparedCredentialAsync(
                            provider.Value, entry, @namespace, key, preparedCredential, cancellationToken).ConfigureAwait(false);
                        if (erased.IsFailure)
                            return Result.Failure(erased.Errors);
                        removal = erased.Value;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Remove(key);
                    return Result.Success();
                },
                cancellationToken).ConfigureAwait(false);

            return modified.IsFailure
                ? Result<ConfigurationRemoval>.Failure(modified.Errors)
                : Result<ConfigurationRemoval>.Success(removal);
        }
        catch (OperationCanceledException canceled)
        {
            return Result<ConfigurationRemoval>.Failure(Error.Canceled(canceled));
        }
    }

    public async ValueTask<Result<IReadOnlyList<string>>> ListAsync(string @namespace, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<IReadOnlyList<string>>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        return entries.IsFailure
            ? Result<IReadOnlyList<string>>.Failure(entries.Errors)
            : Result<IReadOnlyList<string>>.Success(
                [.. entries.Value.Keys.OrderBy(name => name, StringComparer.Ordinal)]);
    }
}
