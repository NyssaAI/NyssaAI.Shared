// Responsibility: Public write contract for storing, merging, and removing configuration values.
// Public interface:
// `-- ConfigurationWriter
//     +-- mergeSettings(namespace, value, signal)
//     +-- remove(namespace, key, signal)
//     +-- set(namespace, key, value, options, signal)
//     +-- setAs(namespace, key, value, options, signal)
//     `-- setNamespace(namespace, value, signal)

import { type Result } from "./results/Result.js";
import type { SetOptions } from "./SetOptions.js";
import type { ConfigurationRemoval } from "./values/ConfigurationRemoval.js";

/**
 * ConfigurationWriter provides methods for storing, replacing, merging, and removing configuration values.
 */
export interface ConfigurationWriter {
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
   * Removes a configuration key from a namespace and its backing provider.
   */
  remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<ConfigurationRemoval>>;
}
