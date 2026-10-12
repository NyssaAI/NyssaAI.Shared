using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Client;

/// <summary>Delegates configuration workflows to the composed entry store, and owns what the composition opened.</summary>
internal sealed class ConfigurationClient : IConfiguration, IConfigurationExtensions, IDisposable
{
    private readonly IConfigurationValues _entries;
    private readonly IDisposable? _owned;

    public ConfigurationClient(IConfigurationValues entries, IDisposable? owned = null)
    {
        _entries = entries;
        _owned = owned;
    }

    public ValueTask<Result<string>> GetAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default) =>
        _entries.GetAsync(@namespace, key, cancellationToken);

    public ValueTask<Result> SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default) =>
        _entries.SetAsync(@namespace, key, value, provider, cancellationToken);

    public ValueTask<Result> RemoveAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default) =>
        _entries.RemoveAsync(@namespace, key, cancellationToken);

    public ValueTask<Result<IReadOnlyList<string>>> ListAsync(
        string @namespace,
        CancellationToken cancellationToken = default) =>
        _entries.ListAsync(@namespace, cancellationToken);

    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask<Result<TValue>> GetAsAsync<TValue>(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default) =>
        _entries.GetAsAsync<TValue>(@namespace, key, cancellationToken);

    public ValueTask<Result<TValue>> GetAsAsync<TValue>(
        string @namespace,
        string key,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default) =>
        _entries.GetAsAsync(@namespace, key, typeInfo, cancellationToken);

    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask<Result> SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default) =>
        _entries.SetAsAsync(@namespace, key, value, provider, cancellationToken);

    public ValueTask<Result> SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        JsonTypeInfo<TValue> typeInfo,
        string? provider = null,
        CancellationToken cancellationToken = default) =>
        _entries.SetAsAsync(@namespace, key, value, typeInfo, provider, cancellationToken);

    public ValueTask<Result<ConfigurationMove>> MoveAsync(
        string @namespace,
        string key,
        string toProvider,
        CancellationToken cancellationToken = default) =>
        _entries.MoveAsync(@namespace, key, toProvider, cancellationToken);

    // Explicit, per SP10d: the extension surface stays public and documented, but a caller reaches
    // it only by naming IConfigurationExtensions, which is the signal they are the second audience.
    ValueTask<Result> IConfigurationExtensions.AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken) =>
        _entries.AuthorizeAsync(@namespace, key, options, cancellationToken);

    ValueTask<Result> IConfigurationExtensions.AuthorizeProviderAsync(
        string provider,
        CancellationToken cancellationToken) =>
        _entries.AuthorizeProviderAsync(provider, cancellationToken);

    ValueTask<Result> IConfigurationExtensions.ReauthorizeAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken) =>
        _entries.ReauthorizeAsync(@namespace, key, cancellationToken);

    /// <summary>Releases what the composition opened; the resolving scope calls this, never a consumer.</summary>
    public void Dispose() => _owned?.Dispose();
}
