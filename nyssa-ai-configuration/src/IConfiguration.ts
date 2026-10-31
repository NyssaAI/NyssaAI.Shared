// Responsibility: Legacy I-prefixed compatibility alias for the Configuration contract.
// Public interface:
// `-- IConfiguration
//     +-- authorize(namespace, key, options, signal)
//     +-- authorizeProvider(providerName, signal)
//     +-- get(namespace, key, signal)
//     +-- getAs(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     +-- list(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- move(namespace, key, destinationProvider, signal)
//     +-- reauthorize(namespace, key, signal)
//     +-- recover(handle, signal)
//     +-- remove(namespace, key, signal)
//     +-- set(namespace, key, value, options, signal)
//     +-- setAs(namespace, key, value, options, signal)
//     `-- setNamespace(namespace, value, signal)

import { type Result } from "./results/Result.js";
import type { SetOptions } from "./SetOptions.js";
import type { ConfigurationRemoval } from "./values/ConfigurationRemoval.js";
import type { ConfigurationMove } from "./values/ConfigurationMove.js";
import type { OAuthOptions } from "./oauth/OAuthOptions.js";
import type { ConfigurationWriteRecoveryHandle } from "./recovery/ConfigurationWriteRecoveryHandle.js";

/**
 * @deprecated Prefer `Configuration` (or narrower `ConfigurationReader`, `ConfigurationWriter`, `ConfigurationOAuthWriter`, `ConfigurationMaintainer`).
 */
export interface IConfiguration {
  get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<string>>;
  getAs<T>(namespace: string, key: string, signal?: AbortSignal): Promise<Result<T>>;
  getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>>;
  set(
    namespace: string,
    key: string,
    value: string,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;
  setAs<T>(
    namespace: string,
    key: string,
    value: T,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;
  setNamespace<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>>;
  mergeSettings<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>>;
  list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>>;
  remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<ConfigurationRemoval>>;
  move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>>;
  authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;
  authorizeProvider(providerName: string, signal?: AbortSignal): Promise<Result<void>>;
  reauthorize(namespace: string, key: string, signal?: AbortSignal): Promise<Result<void>>;
  recover(handle: ConfigurationWriteRecoveryHandle, signal?: AbortSignal): Promise<Result<void>>;
}
