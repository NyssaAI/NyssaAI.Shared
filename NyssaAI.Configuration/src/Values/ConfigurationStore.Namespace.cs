using System.Text;
using System.Text.Json;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

internal sealed partial class ConfigurationStore
{
    private const string LegacySettingsKey = "namespaceSettings";

    private async ValueTask<Result<string>> GetNamespaceAsync(string @namespace, CancellationToken cancellationToken)
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
            return Result<string>.Failure(EntryNotFound(@namespace, LegacySettingsKey));
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
                writer.WriteStringValue(resolved.Value);
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
            : await ReadThroughAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Result> SetNamespaceAsync(string @namespace, string serialized, CancellationToken cancellationToken)
    {
        try
        {
            return await WriteNamespaceAsync(@namespace, serialized, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            return Result.Failure(Error.Canceled(canceled));
        }
    }

    private async ValueTask<Result> WriteNamespaceAsync(string @namespace, string serialized, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(serialized);
        // Preserve the earlier scalar shorthand; objects use one entry per serialized property.
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            return await SetAsync(@namespace, LegacySettingsKey, serialized, IConfiguration.Plain, cancellationToken).ConfigureAwait(false);
        var properties = json.RootElement.EnumerateObject().ToArray();
        if (properties.Any(property => string.Equals(property.Name, LegacySettingsKey, StringComparison.Ordinal))
            || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            return Result.Failure(NamespaceJsonInvalid);

        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);
        return await document.Value.ModifyAsync(async entries =>
        {
            if (entries.TryGetValue(LegacySettingsKey, out var legacy) && !legacy.IsLiteral)
                return Result.Failure(AlreadyStoredElsewhere(@namespace, LegacySettingsKey, legacy.Provider));
            // Validate every destination before making any remote writes.
            foreach (var property in properties)
            {
                if (!entries.TryGetValue(property.Name, out var entry))
                    continue;
                var valid = entry.Validate();
                if (valid.IsFailure)
                    return valid;
                if (!entry.IsLiteral)
                {
                    var provider = GetProvider(entry.Provider);
                    if (provider.IsFailure)
                        return Result.Failure(provider.Errors);
                    var circular = CheckCircular(entry.Provider, @namespace, property.Name);
                    if (circular.IsFailure)
                        return circular;
                }
            }

            foreach (var property in properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isJson = property.Value.ValueKind != JsonValueKind.String;
                var value = isJson ? property.Value.GetRawText() : property.Value.GetString()!;
                if (!entries.TryGetValue(property.Name, out var existing) || existing.IsLiteral)
                    entries[property.Name] = new ConfigurationValue { Value = value, IsJsonValue = isJson };
                else
                {
                    var provider = GetProvider(existing.Provider);
                    var written = await provider.Value.WriteAsync(value, @namespace, property.Name, cancellationToken).ConfigureAwait(false);
                    if (written.IsFailure)
                        return Result.Failure(written.Errors);
                    entries[property.Name] = written.Value with { IsJsonValue = isJson };
                }
            }
            entries.Remove(LegacySettingsKey);
            return Result.Success();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Error NamespaceJsonInvalid => Error.FromMessages(
        "The namespace settings could not be processed.",
        "Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
        ErrorCategory.Validation);
}
