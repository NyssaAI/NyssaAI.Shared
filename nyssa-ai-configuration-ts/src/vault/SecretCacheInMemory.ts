// Responsibility: In-memory SecretCache implementation with injectable clock and TTL expiration.
// Public interface:
// `-- SecretCacheInMemory
//     +-- clear()
//     +-- get(key)
//     +-- new(clock)
//     +-- remove(key)
//     `-- set(key, value, ttlSeconds)

import type { SecretCache } from "./SecretCache.js";

/**
 * SecretCacheInMemory stores cached vault secrets in a process-local Map with millisecond expiration.
 */
export class SecretCacheInMemory {
  private readonly _clock: () => number;
  private readonly _entries = new Map<string, { readonly value: string; readonly expiresAtMs: number }>();

  public constructor(clock?: () => number) {
    this._clock = clock ?? Date.now;
  }

  public get(key: string): string | undefined {
    const entry = this._entries.get(key);
    if (!entry) {
      return undefined;
    }
    if (this._clock() >= entry.expiresAtMs) {
      this._entries.delete(key);
      return undefined;
    }
    return entry.value;
  }

  public set(key: string, value: string, ttlSeconds: number): void {
    if (ttlSeconds <= 0) {
      this._entries.delete(key);
      return;
    }
    this._entries.set(key, {
      value,
      expiresAtMs: this._clock() + ttlSeconds * 1000
    });
  }

  public remove(key: string): void {
    this._entries.delete(key);
  }

  public clear(): void {
    this._entries.clear();
  }
}

const _typeCheck: SecretCache = null as unknown as SecretCacheInMemory;
void _typeCheck;
