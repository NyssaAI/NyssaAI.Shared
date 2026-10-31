using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.Values;
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

    public ValueTask<string> GetAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.GetAsync(@namespace, key, cancellationToken), cancellationToken);
    }

    public ValueTask SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.SetAsync(@namespace, key, value, provider, cancellationToken), cancellationToken);
    }

    public ValueTask<ConfigurationRemoval> RemoveAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.RemoveAsync(@namespace, key, cancellationToken), cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> ListAsync(
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.ListAsync(@namespace, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask MergeSettingsAsync<TValue>(
        string @namespace,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.MergeSettingsAsync(@namespace, value, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask MergeSettingsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.MergeSettingsAsync(@namespace, typeInfo, value, cancellationToken), cancellationToken);
    }

    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.GetAsAsync<TValue>(@namespace, null, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.GetAsAsync(@namespace, null, typeInfo, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask SetAsAsync<TValue>(
        string @namespace,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.SetAsAsync(@namespace, null, value, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SetAsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.SetAsAsync(@namespace, null, value, typeInfo, cancellationToken: cancellationToken), cancellationToken);
    }

    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.GetAsAsync<TValue>(@namespace, key, cancellationToken), cancellationToken);
    }

    public ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        string key,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.GetAsAsync(@namespace, key, typeInfo, cancellationToken), cancellationToken);
    }

    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    public ValueTask SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.SetAsAsync(@namespace, key, value, provider, cancellationToken), cancellationToken);
    }

    public ValueTask SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        JsonTypeInfo<TValue> typeInfo,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.SetAsAsync(@namespace, key, value, typeInfo, provider, cancellationToken), cancellationToken);
    }

    public ValueTask<ConfigurationMove> MoveAsync(
        string @namespace,
        string key,
        string? toProvider = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.MoveAsync(@namespace, key, toProvider, cancellationToken), cancellationToken);
    }


    public ValueTask<ConfigurationRecoveryOutcome> RecoverAsync(ConfigurationRecoveryHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        cancellationToken.ThrowIfCancellationRequested();
        return UnwrapAsync(_entries.RecoverAsync(handle, cancellationToken), cancellationToken);
    }

    private static async ValueTask UnwrapAsync(ValueTask<ConfigurationAttempt> operation, CancellationToken token)
    {
        var attempt = await operation.ConfigureAwait(false);
        ThrowPartial(attempt.Errors, attempt.Progress, attempt.RecoveryHandle, token);
        ThrowIfCanceled(attempt.Errors, token);
        attempt.Result.ThrowIfError();
    }

    private static async ValueTask<TValue> UnwrapAsync<TValue>(ValueTask<ConfigurationAttempt<TValue>> operation, CancellationToken token)
    {
        var attempt = await operation.ConfigureAwait(false);
        ThrowPartial(attempt.Errors, attempt.Progress, attempt.RecoveryHandle, token);
        ThrowIfCanceled(attempt.Errors, token);
        return attempt.Result.ThrowIfError();
    }

    private static void ThrowPartial(IReadOnlyList<Error> errors, ConfigurationOperationProgress? progress, ConfigurationRecoveryHandle? handle, CancellationToken token)
    {
        if (errors.Count == 0 || progress is null) return;
        if (errors.Any(error => error.Category == ErrorCategory.Canceled))
            throw new ConfigurationOperationCanceledException(progress, handle, token);
        throw new ConfigurationOperationException(errors, progress, handle);
    }

    private static async ValueTask<TValue> UnwrapAsync<TValue>(ValueTask<Result<TValue>> operation, CancellationToken cancellationToken)
    {
        var result = await operation.ConfigureAwait(false);
        ThrowIfCanceled(result.Errors, cancellationToken);
        return result.ThrowIfError();
    }

    private static async ValueTask UnwrapAsync(ValueTask<Result> operation, CancellationToken cancellationToken)
    {
        var result = await operation.ConfigureAwait(false);
        ThrowIfCanceled(result.Errors, cancellationToken);
        result.ThrowIfError();
    }

    private static void ThrowIfCanceled(IReadOnlyList<Error> errors, CancellationToken cancellationToken)
    {
        if (errors.Any(error => error.Category == ErrorCategory.Canceled))
            throw new OperationCanceledException(cancellationToken);
    }

    // Explicit, per SP10d: the extension surface stays public and documented, but a caller reaches
    // it only by naming IConfigurationOAuthWriter or IConfigurationExtensions, which is the signal they are the second audience.
    ValueTask<Result> IConfigurationOAuthWriter.AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken) =>
        _entries.AuthorizeAsync(@namespace, key, options, cancellationToken);

    ValueTask<Result> IConfigurationOAuthWriter.AuthorizeProviderAsync(
        string provider,
        CancellationToken cancellationToken) =>
        _entries.AuthorizeProviderAsync(provider, cancellationToken);

    ValueTask<Result> IConfigurationOAuthWriter.ReauthorizeAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken) =>
        _entries.ReauthorizeAsync(@namespace, key, cancellationToken);

    /// <summary>Releases what the composition opened; the resolving scope calls this, never a consumer.</summary>
    public void Dispose() => _owned?.Dispose();
}
