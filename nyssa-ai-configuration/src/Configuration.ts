// Responsibility: Public contract for reading, writing, listing, migrating, authorizing, and recovering configuration values.
// Public interface:
// `-- Configuration
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
 * Configuration provides application code with access to persistent settings, secrets, OAuth credentials, and recovery.
 */
export interface Configuration {
  /**
   * Reads a string or primitive configuration value.
   */
  get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<string>>;

  /**
   * Reads a typed object or primitive configuration value under a single key.
   */
  getAs<T>(namespace: string, key: string, signal?: AbortSignal): Promise<Result<T>>;

  /**
   * Reads and assembles all keys in a namespace into a single typed object.
   */
  getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>>;

  /**
   * Saves a string configuration value.
   */
  set(
    namespace: string,
    key: string,
    value: string,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Saves a typed object or primitive configuration value under a single key.
   */
  setAs<T>(
    namespace: string,
    key: string,
    value: T,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Replaces a namespace with the top-level properties of a typed object (preserving existing providers on surviving keys).
   */
  setNamespace<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Merges the top-level properties of a typed object into an existing namespace without removing omitted keys.
   */
  mergeSettings<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Lists all keys in a namespace in strict ordinal code-unit order without decrypting values.
   */
  list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>>;

  /**
   * Removes a configuration key from a namespace and its backing provider.
   */
  remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<ConfigurationRemoval>>;

  /**
   * Migrates an existing configuration key to a new provider, writing to the destination first and withdrawing the source copy.
   */
  move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>>;

  /**
   * Acquires and stores an OAuth 2.0 Authorization Code + PKCE grant under the specified namespace and key.
   */
  authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Acquires and stores an OAuth 2.0 Authorization Code + PKCE grant for a configured cloud key vault provider.
   */
  authorizeProvider(providerName: string, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Re-runs interactive OAuth 2.0 PKCE sign-in using endpoint metadata already stored at (namespace, key).
   */
  reauthorize(namespace: string, key: string, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Replays an interrupted multi-step write using its portable recovery handle.
   */
  recover(handle: ConfigurationWriteRecoveryHandle, signal?: AbortSignal): Promise<Result<void>>;
}
