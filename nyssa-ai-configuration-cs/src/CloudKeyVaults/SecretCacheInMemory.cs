using System.Collections.Concurrent;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>
/// Thread-safe in-memory <see cref="ISecretCache"/> implementation backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class SecretCacheInMemory : ISecretCache
{
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new in-memory secret cache using <paramref name="clock"/> (or <see cref="TimeProvider.System"/> when omitted).
    /// </summary>
    /// <param name="clock">Optional time provider for deterministic expiration testing.</param>
    public SecretCacheInMemory(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public string? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_entries.TryGetValue(key, out var entry))
            return null;

        if (_clock.GetUtcNow() >= entry.ExpiresAt)
        {
            _entries.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }

    /// <inheritdoc/>
    public void Set(string key, string value, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (ttl <= TimeSpan.Zero)
        {
            _entries.TryRemove(key, out _);
            return;
        }

        _entries[key] = (value, _clock.GetUtcNow().Add(ttl));
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _entries.TryRemove(key, out _);
    }

    /// <inheritdoc/>
    public void Clear() => _entries.Clear();
}
