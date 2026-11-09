// Responsibility: Decorator caching IVaultProvider secret reads with a configurable TTL (cacheTTLSeconds) and pluggable SecretCache.
// Public interface:
// `-- VaultProviderCaching
//     +-- DEFAULT_CACHE_TTL_SECONDS
//     +-- cacheScope
//     +-- cacheTTLSeconds
//     +-- clearCache()
//     +-- deleteSecret(secretName, signal)
//     +-- execute(operation, preparation, signal)
//     +-- getSecret(secretName, signal)
//     +-- name
//     +-- new(inner, cacheTTLSeconds, cache, cacheScope, clock)
//     +-- prepare(operation, signal)
//     +-- reconcile(operation, preparation, signal)
//     +-- recovery
//     +-- resolveTtlSeconds(options)
//     +-- setSecret(secretName, secretValue, signal)
//     +-- storeIdentity
//     `-- wrap(inner, options, cache, clock)

import { ok, fail, type Result } from "@nyssa-ai/result";
import type { VaultOptions } from "../manifest/VaultOptions.js";
import type { IVaultProvider } from "./IVaultProvider.js";
import type { CloudKeyVaultRecovery } from "./CloudKeyVaultRecovery.js";
import type {
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  CloudKeyVaultStepEffect
} from "./Types.js";
import type { SecretCache } from "./SecretCache.js";
import { SecretCacheInMemory } from "./SecretCacheInMemory.js";
import { VaultProviderBitwarden } from "./bitwarden/VaultProviderBitwarden.js";

/**
 * VaultProviderCaching decorates an IVaultProvider with TTL secret caching and write/delete/recovery invalidation.
 */
export class VaultProviderCaching {
  public static readonly DEFAULT_CACHE_TTL_SECONDS = 60;

  public readonly name: string;
  public readonly cacheTTLSeconds: number;
  public readonly cacheScope: string;

  private readonly _inner: IVaultProvider;
  private readonly _cache: SecretCache;

  public constructor(
    inner: IVaultProvider,
    cacheTTLSeconds: number = VaultProviderCaching.DEFAULT_CACHE_TTL_SECONDS,
    cache?: SecretCache,
    cacheScope?: string,
    clock?: () => number
  ) {
    this._inner = inner;
    this.name = inner.name;
    this.cacheTTLSeconds = Math.max(0, Math.trunc(cacheTTLSeconds));
    this._cache = cache ?? new SecretCacheInMemory(clock);
    const trimmedScope = cacheScope?.trim();
    this.cacheScope = trimmedScope && trimmedScope.length > 0 ? trimmedScope : inner.name;

    if (this.cacheTTLSeconds > 0 && inner instanceof VaultProviderBitwarden) {
      inner.setSecretListedCallback((secretKey, secretValue) => {
        void this.safeSet(this.formatKey(secretKey), secretValue);
      });
    }
  }

  public get recovery(): CloudKeyVaultRecovery | undefined {
    return this._inner.recovery ? this : undefined;
  }

  public get storeIdentity(): string {
    return this._inner.recovery?.storeIdentity ?? "";
  }

  public static resolveTtlSeconds(options: VaultOptions): Result<number> {
    if (options.cacheTTLSeconds !== undefined && options.cacheTTLSeconds !== null) {
      if (
        typeof options.cacheTTLSeconds !== "number" ||
        !Number.isInteger(options.cacheTTLSeconds) ||
        options.cacheTTLSeconds < 0
      ) {
        return fail(
          `vault: '${options.name}' cacheTTLSeconds must be a non-negative integer.`,
          "Validation"
        );
      }
      return ok(options.cacheTTLSeconds);
    }

    const rawSetting = options.settings?.["cacheTTLSeconds"];
    if (rawSetting !== undefined) {
      const trimmed = rawSetting.trim();
      if (!/^\d+$/.test(trimmed)) {
        return fail(
          `vault: '${options.name}' settings['cacheTTLSeconds'] must be a non-negative integer.`,
          "Validation"
        );
      }
      return ok(Number.parseInt(trimmed, 10));
    }

    return ok(VaultProviderCaching.DEFAULT_CACHE_TTL_SECONDS);
  }

  public static wrap(
    inner: IVaultProvider,
    options: VaultOptions,
    cache?: SecretCache,
    clock?: () => number
  ): Result<IVaultProvider> {
    const ttlRes = VaultProviderCaching.resolveTtlSeconds(options);
    if (!ttlRes.ok) return ttlRes;
    if (ttlRes.value === 0) return ok(inner);

    const settings = options.settings ?? {};
    const targetId =
      settings["projectId"]?.trim() ||
      options.endpoint?.trim() ||
      settings["serverUrl"]?.trim() ||
      settings["vaultUri"]?.trim() ||
      "";
    const scope = targetId.length > 0 ? `${options.name}:${options.kind}:${targetId}` : `${options.name}:${options.kind}`;
    return ok(new VaultProviderCaching(inner, ttlRes.value, cache, scope, clock));
  }

  public async getSecret(secretName: string, signal?: AbortSignal): Promise<Result<string>> {
    signal?.throwIfAborted();
    const trimmed = secretName.trim();
    if (this.cacheTTLSeconds <= 0 || trimmed.length === 0) {
      return this._inner.getSecret(secretName, signal);
    }

    const key = this.formatKey(trimmed);
    const cached = await this.safeGet(key);
    if (cached !== undefined) {
      return ok(cached);
    }

    const res = await this._inner.getSecret(secretName, signal);
    if (res.ok && typeof res.value === "string") {
      await this.safeSet(key, res.value);
    }
    return res;
  }

  public async setSecret(secretName: string, secretValue: string, signal?: AbortSignal): Promise<Result<void>> {
    await this.evictPointer(secretName);
    const res = await this._inner.setSecret(secretName, secretValue, signal);
    await this.evictPointer(secretName);
    return res;
  }

  public async deleteSecret(
    secretName: string,
    signal?: AbortSignal
  ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
    await this.evictPointer(secretName);
    return this._inner.deleteSecret(secretName, signal);
  }

  public async clearCache(): Promise<void> {
    try {
      await this._cache.clear();
    } catch {
      // Fail-open on cache backend errors
    }
  }

  public async prepare(
    operation: CloudKeyVaultOperation,
    signal?: AbortSignal
  ): Promise<Result<CloudKeyVaultPreparation>> {
    if (!this._inner.recovery) {
      return fail(`vault: '${this.name}' does not support two-phase recovery.`, "Validation");
    }
    return this._inner.recovery.prepare(operation, signal);
  }

  public async execute(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation,
    signal?: AbortSignal
  ): Promise<Result<CloudKeyVaultStepEffect>> {
    if (!this._inner.recovery) {
      return fail(`vault: '${this.name}' does not support two-phase recovery.`, "Validation");
    }
    await this.evictRecoveryKeys(operation, preparation);
    const res = await this._inner.recovery.execute(operation, preparation, signal);
    if (res.ok && res.value.pointer?.value) {
      await this.evictPointer(res.value.pointer.value);
    }
    return res;
  }

  public async reconcile(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation,
    signal?: AbortSignal
  ): Promise<Result<CloudKeyVaultStepEffect>> {
    if (!this._inner.recovery) {
      return fail(`vault: '${this.name}' does not support two-phase recovery.`, "Validation");
    }
    await this.evictRecoveryKeys(operation, preparation);
    const res = await this._inner.recovery.reconcile(operation, preparation, signal);
    if (res.ok && res.value.pointer?.value) {
      await this.evictPointer(res.value.pointer.value);
    }
    return res;
  }

  private async evictRecoveryKeys(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation
  ): Promise<void> {
    await this.evictPointer(`${operation.namespace.trim()}/${operation.key.trim()}`);
    if (operation.pointer?.value) {
      await this.evictPointer(operation.pointer.value);
    }
    if (preparation.targetReference) {
      await this.evictPointer(preparation.targetReference);
    }
  }

  private async evictPointer(pointerValue: string): Promise<void> {
    const trimmed = pointerValue.trim();
    if (trimmed.length === 0) return;
    await this.safeRemove(this.formatKey(trimmed));
  }

  private formatKey(pointerValue: string): string {
    return `${this.cacheScope}:${this.name}:${pointerValue}`;
  }

  private async safeGet(key: string): Promise<string | undefined> {
    try {
      return await this._cache.get(key);
    } catch {
      return undefined;
    }
  }

  private async safeSet(key: string, value: string): Promise<void> {
    try {
      await this._cache.set(key, value, this.cacheTTLSeconds);
    } catch {
      // Fail-open
    }
  }

  private async safeRemove(key: string): Promise<void> {
    try {
      await this._cache.remove(key);
    } catch {
      // Fail-open
    }
  }
}

const _typeCheck: IVaultProvider = null as unknown as VaultProviderCaching;
void _typeCheck;
const _recoveryTypeCheck: CloudKeyVaultRecovery = null as unknown as VaultProviderCaching;
void _recoveryTypeCheck;
