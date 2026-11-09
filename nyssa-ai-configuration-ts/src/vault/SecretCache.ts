// Responsibility: Contract for pluggable in-memory or cross-process vault secret caching with TTL expiration.
// Public interface:
// `-- SecretCache
//     +-- clear()
//     +-- get(key)
//     +-- remove(key)
//     `-- set(key, value, ttlSeconds)

/**
 * SecretCache defines a pluggable cache store for resolved vault secrets (in-memory or OS keyring).
 */
export interface SecretCache {
  /** Returns the cached cleartext secret for key when present and unexpired, or undefined. */
  get(key: string): string | undefined | Promise<string | undefined>;

  /** Stores value under key for ttlSeconds. */
  set(key: string, value: string, ttlSeconds: number): void | Promise<void>;

  /** Evicts key from the cache if present. */
  remove(key: string): void | Promise<void>;

  /** Evicts all cached entries. */
  clear(): void | Promise<void>;
}
