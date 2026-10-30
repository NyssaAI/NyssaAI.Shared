using System.Text;
using System.Text.Json;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// NamespaceDocumentHandler manages reading, writing, merging, and migration
/// of full-namespace JSON documents in configuration storage.
/// </summary>
internal sealed class NamespaceDocumentHandler
{
    private const string LegacySettingsKey = "namespaceSettings";

    private static Error NamespaceObjectRequired => Error.FromMessages(
        "The namespace settings could not be merged.",
        "Merging namespace settings requires JSON objects for the supplied value and any legacy namespaceSettings value.",
        ErrorCategory.Validation);

    private static Error NamespaceJsonInvalid => Error.FromMessages(
        "The namespace settings could not be processed.",
        "Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
        ErrorCategory.Validation);

    private static Error RecoveryUnfinished => Error.FromMessage(
        "The configuration operation is unfinished.", ErrorCategory.Unavailable);

    private readonly IConfigurationDocumentFactory _documents;
    private readonly Func<ConfigurationValue, string, string, CancellationToken, ValueTask<Result<string>>> _readThrough;
    private readonly Func<string, Result<IConfigurationProvider>> _getProvider;
    private readonly Func<string, string, string, Result> _checkCircular;
    private readonly ConfigurationRecoveryCoordinator _recoveryCoordinator;

    public NamespaceDocumentHandler(
        IConfigurationDocumentFactory documents,
        Func<ConfigurationValue, string, string, CancellationToken, ValueTask<Result<string>>> readThrough,
        Func<string, Result<IConfigurationProvider>> getProvider,
        Func<string, string, string, Result> checkCircular,
        ConfigurationRecoveryCoordinator recoveryCoordinator)
    {
        _documents = documents;
        _readThrough = readThrough;
        _getProvider = getProvider;
        _checkCircular = checkCircular;
        _recoveryCoordinator = recoveryCoordinator;
    }

    public async ValueTask<Result<string>> GetNamespaceAsync(string @namespace, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadNamespaceAsync(@namespace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            return Result<string>.Failure(Error.Canceled(canceled));
        }
    }

    private async ValueTask<Result<string>> ReadNamespaceAsync(string @namespace, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);
        var read = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
            return Result<string>.Failure(read.Errors);
        if (read.Value.Count == 0)
            return Result<string>.Failure(ConfigurationStore.EntryNotFound(@namespace, LegacySettingsKey));

        // Old files remain readable until a namespace object write migrates their plaintext DTO.
        if (read.Value.TryGetValue(LegacySettingsKey, out var legacy))
            return await ResolveNamespaceEntryAsync(legacy, @namespace, LegacySettingsKey, cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        foreach (var (key, entry) in read.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveNamespaceEntryAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
                return Result<string>.Failure(resolved.Errors);
            writer.WritePropertyName(key);
            if (entry.IsJsonValue)
            {
                try
                {
                    using var json = JsonDocument.Parse(resolved.Value);
                    json.RootElement.WriteTo(writer);
                }
                catch (JsonException)
                {
                    return Result<string>.Failure(NamespaceJsonInvalid);
                }
            }
            else
            {
                writer.WriteStringValue(resolved.Value);
            }
        }
        writer.WriteEndObject();
        writer.Flush();
        return Result<string>.Success(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private async ValueTask<Result<string>> ResolveNamespaceEntryAsync(
        ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken)
    {
        var valid = entry.Validate();
        if (valid.IsFailure)
            return Result<string>.Failure(valid.Errors);
        return entry.IsLiteral
            ? Result<string>.Success(entry.Value)
            : await _readThrough(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ConfigurationAttempt> SetNamespaceAsync(
        string @namespace, string serialized, bool replace, CancellationToken cancellationToken)
    {
        try
        {
            return await WriteNamespaceAsync(@namespace, serialized, replace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            return Result.Failure(Error.Canceled(canceled));
        }
        catch (JsonException)
        {
            return Result.Failure(NamespaceJsonInvalid);
        }
    }

    private async ValueTask<ConfigurationAttempt> WriteNamespaceAsync(
        string @namespace, string serialized, bool replace, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(serialized);
        var isObject = json.RootElement.ValueKind == JsonValueKind.Object;
        Dictionary<string, ConfigurationValue> incoming;
        if (isObject)
        {
            var properties = ReadNamespaceProperties(json.RootElement);
            if (properties.IsFailure)
                return Result.Failure(properties.Errors);
            incoming = properties.Value;
        }
        else
        {
            if (!replace)
                return Result.Failure(NamespaceObjectRequired);
            // Preserve scalar shorthand, but replace the namespace with only its legacy entry.
            incoming = new(StringComparer.Ordinal)
            {
                [LegacySettingsKey] = new ConfigurationValue { Value = serialized },
            };
        }

        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);
        ConfigurationRecoveryState? state = null;
        var publishing = false;
        Result result;
        try
        {
            result = await document.Value.ModifyAsync(async entries =>
            {
                var original = ConfigurationRecoveryCoordinator.Snapshot(entries);
                if (isObject && entries.TryGetValue(LegacySettingsKey, out var legacy))
                {
                    if (!legacy.IsLiteral)
                        return Result.Failure(ConfigurationStore.AlreadyStoredElsewhere(@namespace, LegacySettingsKey, legacy.Provider));
                    if (!replace)
                    {
                        // Merge must preserve properties that existed only in the legacy object.
                        using var legacyJson = JsonDocument.Parse(legacy.Value);
                        var properties = ReadNamespaceProperties(legacyJson.RootElement);
                        if (properties.IsFailure)
                            return Result.Failure(properties.Errors);
                        foreach (var (key, value) in properties.Value)
                            entries.TryAdd(key, value);
                    }
                }
                // Validate every destination before making any remote writes.
                foreach (var key in incoming.Keys)
                {
                    if (!entries.TryGetValue(key, out var entry))
                        continue;
                    var valid = entry.Validate();
                    if (valid.IsFailure)
                        return valid;
                    if (!entry.IsLiteral)
                    {
                        var provider = _getProvider(entry.Provider);
                        if (provider.IsFailure)
                            return Result.Failure(provider.Errors);
                        var circular = _checkCircular(entry.Provider, @namespace, key);
                        if (circular.IsFailure)
                            return circular;
                    }
                }

                var steps = new List<ConfigurationRecoveryStep>();
                foreach (var (key, value) in incoming)
                {
                    entries.TryGetValue(key, out var existing);
                    var step = await _recoveryCoordinator.PrepareStepAsync(existing?.Provider ?? string.Empty, CloudKeyVaultAction.Write,
                        @namespace, key, value.Value, existing, value.IsJsonValue, cancellationToken).ConfigureAwait(false);
                    if (step.IsFailure) return Result.Failure(step.Errors);
                    steps.Add(step.Value);
                }
                state = new ConfigurationRecoveryState
                {
                    OperationId = Guid.NewGuid(), Namespace = @namespace,
                    Kind = replace ? ConfigurationOperationKind.Replace : ConfigurationOperationKind.Merge,
                    Original = original, Proposed = ConfigurationRecoveryCoordinator.Snapshot(entries), Steps = steps,
                };
                if (replace)
                {
                    foreach (var key in state.Proposed.Keys.Where(key => !incoming.ContainsKey(key)).ToArray())
                        state.Proposed.Remove(key);
                }
                else state.Proposed.Remove(LegacySettingsKey);
                var written = await _recoveryCoordinator.WriteStepsAsync(state, cancellationToken).ConfigureAwait(false);
                if (written.IsFailure) return written;
                entries.Clear();
                foreach (var entry in state.Proposed) entries.Add(entry.Key, entry.Value);
                cancellationToken.ThrowIfCancellationRequested();
                publishing = true;
                state = state with { Publication = ConfigurationPublicationState.Unknown };
                return Result.Success();
            }, cancellationToken).ConfigureAwait(false);
            if (state is not null)
                state = state with { Publication = result.IsSuccess ? ConfigurationPublicationState.Published
                    : publishing ? ConfigurationPublicationState.Failed : ConfigurationPublicationState.NotAttempted };
        }
        catch (OperationCanceledException)
        {
            result = Result.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (JsonException) { result = Result.Failure(NamespaceJsonInvalid); }
        catch (Exception) { result = Result.Failure(RecoveryUnfinished); }
        return _recoveryCoordinator.Attempt(result, state);
    }

    private static Result<Dictionary<string, ConfigurationValue>> ReadNamespaceProperties(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return Result<Dictionary<string, ConfigurationValue>>.Failure(NamespaceObjectRequired);
        var properties = new Dictionary<string, ConfigurationValue>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var isJson = property.Value.ValueKind != JsonValueKind.String;
            var entry = new ConfigurationValue
            {
                Value = isJson ? property.Value.GetRawText() : property.Value.GetString()!,
                IsJsonValue = isJson,
            };
            if (string.Equals(property.Name, LegacySettingsKey, StringComparison.Ordinal)
                || !properties.TryAdd(property.Name, entry))
                return Result<Dictionary<string, ConfigurationValue>>.Failure(NamespaceJsonInvalid);
        }
        return Result<Dictionary<string, ConfigurationValue>>.Success(properties);
    }
}
