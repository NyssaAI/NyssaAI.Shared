namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// Pluggable cache store for resolved cloud key vault secrets with a time-to-live (TTL),
/// supporting both in-memory process caches and cross-process OS keyring caches.
/// </summary>
public interface ISecretCache
{
    /// <summary>
    /// Returns the cached cleartext secret for <paramref name="key"/> when present and unexpired, or <see langword="null"/>.
    /// </summary>
    /// <param name="key">The scoped cache key.</param>
    string? Get(string key);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="ttl"/>.
    /// </summary>
    /// <param name="key">The scoped cache key.</param>
    /// <param name="value">The cleartext secret value.</param>
    /// <param name="ttl">The time-to-live duration for the cached entry.</param>
    void Set(string key, string value, TimeSpan ttl);

    /// <summary>
    /// Evicts <paramref name="key"/> from the cache if present.
    /// </summary>
    /// <param name="key">The scoped cache key to evict.</param>
    void Remove(string key);

    /// <summary>
    /// Evicts all entries from the cache.
    /// </summary>
    void Clear();
}
