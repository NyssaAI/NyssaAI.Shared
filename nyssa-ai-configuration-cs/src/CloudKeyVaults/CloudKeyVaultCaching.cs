using System.Globalization;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.CloudKeyVaults.Bitwarden;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>
/// Decorates an <see cref="ICloudKeyVault"/> with a configurable TTL secret cache (<c>cacheTTLSeconds</c>, default 60s)
/// and automatic cache invalidation across writes, removals, and two-phase recovery mutations.
/// </summary>
public sealed class CloudKeyVaultCaching : ICloudKeyVault, ICloudKeyVaultRecovery
{
    /// <summary>The default cache time-to-live in seconds when <c>cacheTTLSeconds</c> is not specified.</summary>
    public const int DefaultCacheTTLSeconds = 60;

    private readonly ICloudKeyVault _inner;
    private readonly ISecretCache _cache;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Initializes a caching decorator around <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The inner cloud key vault adapter.</param>
    /// <param name="cacheTTLSeconds">The cache time-to-live in seconds (0 disables caching).</param>
    /// <param name="cache">Optional custom cache backend (defaults to <see cref="SecretCacheInMemory"/>).</param>
    /// <param name="cacheScope">Optional scope prefix isolating entries across vault targets.</param>
    /// <param name="clock">Optional time provider used when constructing the default in-memory cache.</param>
    public CloudKeyVaultCaching(
        ICloudKeyVault inner,
        int cacheTTLSeconds = DefaultCacheTTLSeconds,
        ISecretCache? cache = null,
        string? cacheScope = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        CacheTTLSeconds = Math.Max(0, cacheTTLSeconds);
        _ttl = TimeSpan.FromSeconds(CacheTTLSeconds);
        _cache = cache ?? new SecretCacheInMemory(clock);
        CacheScope = string.IsNullOrWhiteSpace(cacheScope) ? inner.Name : cacheScope.Trim();

        if (CacheTTLSeconds > 0 && inner is CloudKeyVaultBitwarden bitwarden)
        {
            bitwarden.SetSecretListedCallback((secretKey, secretValue) =>
                SafeSet(FormatKey(inner.Name, secretKey), secretValue));
        }
    }

    /// <summary>Gets the configured provider name of the wrapped vault instance.</summary>
    public string Name => _inner.Name;

    /// <summary>Gets the configured cache time-to-live in seconds.</summary>
    public int CacheTTLSeconds { get; }

    /// <summary>Gets the scope prefix isolating cache entries across vault targets.</summary>
    public string CacheScope { get; }

    /// <summary>Gets the two-phase recovery contract when the inner vault implements <see cref="ICloudKeyVaultRecovery"/>.</summary>
    public ICloudKeyVaultRecovery? Recovery => _inner is ICloudKeyVaultRecovery ? this : null;

    /// <inheritdoc/>
    public string StoreIdentity => (_inner as ICloudKeyVaultRecovery)?.StoreIdentity ?? string.Empty;

    /// <summary>
    /// Resolves and validates <c>cacheTTLSeconds</c> from <paramref name="options"/> or <c>settings["cacheTTLSeconds"]</c>.
    /// </summary>
    /// <param name="options">The vault options to inspect.</param>
    public static Result<int> ResolveTtlSeconds(VaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CacheTTLSeconds < 0)
            return Result<int>.Failure(InvalidCacheTtl(options.Name, options.CacheTTLSeconds.ToString(CultureInfo.InvariantCulture)));

        if (options.Settings.TryGetValue("cacheTTLSeconds", out var rawSetting) && options.CacheTTLSeconds == DefaultCacheTTLSeconds)
        {
            if (!int.TryParse(rawSetting.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                return Result<int>.Failure(InvalidCacheTtl(options.Name, rawSetting));
            return Result<int>.Success(parsed);
        }

        return Result<int>.Success(options.CacheTTLSeconds);
    }

    /// <summary>
    /// Validates <c>cacheTTLSeconds</c> on <paramref name="options"/> and wraps <paramref name="inner"/> in <see cref="CloudKeyVaultCaching"/> when TTL &gt; 0.
    /// </summary>
    /// <param name="inner">The inner vault to wrap.</param>
    /// <param name="options">The vault configuration.</param>
    /// <param name="cache">Optional custom cache backend.</param>
    /// <param name="clock">Optional time provider.</param>
    public static Result<ICloudKeyVault> Wrap(
        ICloudKeyVault inner,
        VaultOptions options,
        ISecretCache? cache = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);

        var ttl = ResolveTtlSeconds(options);
        if (ttl.IsFailure)
            return Result<ICloudKeyVault>.Failure(ttl.Errors);

        if (ttl.Value == 0)
            return Result<ICloudKeyVault>.Success(inner);

        var targetId = TryGetNonBlank(options.Settings, "projectId")
            ?? options.Endpoint?.AbsoluteUri
            ?? TryGetNonBlank(options.Settings, "serverUrl")
            ?? TryGetNonBlank(options.Settings, "vaultUri");
        var scope = string.IsNullOrWhiteSpace(targetId)
            ? $"{options.Name}:{options.Kind}"
            : $"{options.Name}:{options.Kind}:{targetId}";

        return Result<ICloudKeyVault>.Success(new CloudKeyVaultCaching(inner, ttl.Value, cache, scope, clock));
    }

    /// <inheritdoc/>
    public async ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        cancellationToken.ThrowIfCancellationRequested();

        if (CacheTTLSeconds <= 0 || string.IsNullOrWhiteSpace(pointer.Value))
            return await _inner.ReadAsync(pointer, cancellationToken).ConfigureAwait(false);

        var provider = string.IsNullOrWhiteSpace(pointer.Provider) ? _inner.Name : pointer.Provider.Trim();
        var cacheKey = FormatKey(provider, pointer.Value.Trim());
        var cached = SafeGet(cacheKey);
        if (cached is not null)
            return Result<string?>.Success(cached);

        var read = await _inner.ReadAsync(pointer, cancellationToken).ConfigureAwait(false);
        if (read.IsSuccess && read.Value is not null)
            SafeSet(cacheKey, read.Value);

        return read;
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationPointer>> WriteAsync(
        string @namespace,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        EvictPointer($"{@namespace.Trim()}/{key.Trim()}");
        var written = await _inner.WriteAsync(@namespace, key, value, cancellationToken).ConfigureAwait(false);
        if (written.IsSuccess)
            EvictPointer(written.Value.Value);
        return written;
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationRemoval>> RemoveAsync(
        ConfigurationPointer pointer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        EvictPointer(pointer.Value);
        return await _inner.RemoveAsync(pointer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evicts all entries from the underlying <see cref="ISecretCache"/> (fail-open).</summary>
    public void ClearCache()
    {
        try { _cache.Clear(); }
        catch { /* Fail-open */ }
    }

    /// <inheritdoc/>
    public ValueTask<Result<CloudKeyVaultPreparation>> PrepareAsync(
        CloudKeyVaultOperation operation,
        CancellationToken cancellationToken = default)
    {
        if (_inner is not ICloudKeyVaultRecovery recovery)
            return ValueTask.FromResult(Result<CloudKeyVaultPreparation>.Failure(RecoveryUnsupported(_inner.Name)));
        return recovery.PrepareAsync(operation, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<CloudKeyVaultEffect>> ExecuteAsync(
        CloudKeyVaultOperation operation,
        CloudKeyVaultPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        if (_inner is not ICloudKeyVaultRecovery recovery)
            return Result<CloudKeyVaultEffect>.Failure(RecoveryUnsupported(_inner.Name));

        EvictRecoveryKeys(operation, preparation);
        var effect = await recovery.ExecuteAsync(operation, preparation, cancellationToken).ConfigureAwait(false);
        if (effect.IsSuccess && effect.Value.Pointer is not null)
            EvictPointer(effect.Value.Pointer.Value);
        return effect;
    }

    /// <inheritdoc/>
    public async ValueTask<Result<CloudKeyVaultEffect>> ReconcileAsync(
        CloudKeyVaultOperation operation,
        CloudKeyVaultPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        if (_inner is not ICloudKeyVaultRecovery recovery)
            return Result<CloudKeyVaultEffect>.Failure(RecoveryUnsupported(_inner.Name));

        EvictRecoveryKeys(operation, preparation);
        var effect = await recovery.ReconcileAsync(operation, preparation, cancellationToken).ConfigureAwait(false);
        if (effect.IsSuccess && effect.Value.Pointer is not null)
            EvictPointer(effect.Value.Pointer.Value);
        return effect;
    }

    /// <summary>The failure returned when <c>cacheTTLSeconds</c> is negative or not an integer.</summary>
    /// <param name="vault">The vault provider name.</param>
    /// <param name="value">The invalid value.</param>
    public static Error InvalidCacheTtl(string vault, string value) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: vault '{vault}' names cacheTTLSeconds '{value}', which is not a non-negative integer",
        ErrorCategory.Validation);

    private static Error RecoveryUnsupported(string vault) => Error.FromMessages(
        "The configured vault does not support two-phase recovery.",
        $"vault: '{vault}' does not implement ICloudKeyVaultRecovery",
        ErrorCategory.Validation);

    private void EvictRecoveryKeys(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation)
    {
        EvictPointer($"{operation.Namespace.Trim()}/{operation.Key.Trim()}");
        if (operation.Pointer is not null)
            EvictPointer(operation.Pointer.Value);
        if (!string.IsNullOrWhiteSpace(preparation.TargetReference))
            EvictPointer(preparation.TargetReference);
    }

    private void EvictPointer(string? pointerValue)
    {
        if (string.IsNullOrWhiteSpace(pointerValue))
            return;
        SafeRemove(FormatKey(_inner.Name, pointerValue.Trim()));
    }

    private string FormatKey(string provider, string pointerValue) =>
        $"{CacheScope}:{provider}:{pointerValue}";

    private string? SafeGet(string key)
    {
        try { return _cache.Get(key); }
        catch { return null; }
    }

    private void SafeSet(string key, string value)
    {
        try { _cache.Set(key, value, _ttl); }
        catch { /* Fail-open */ }
    }

    private void SafeRemove(string key)
    {
        try { _cache.Remove(key); }
        catch { /* Fail-open */ }
    }

    private static string? TryGetNonBlank(IDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val.Trim() : null;
}
