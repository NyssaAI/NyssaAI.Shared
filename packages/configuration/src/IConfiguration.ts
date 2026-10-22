// Responsibility: Public contract for reading and writing configuration values.
// Public interface:
// +-- IConfiguration
// |   +-- get(namespace, key, signal?)
// |   +-- getAs<T>(namespace, key, signal?)
// |   +-- remove(namespace, key, signal?)
// |   +-- set(namespace, key, value, options?, signal?)
// |   `-- setAs<T>(namespace, key, value, options?, signal?)
// `-- SetOptions
//     +-- encrypted
//     `-- provider

import { type Result } from "./results/Result.js";

export interface SetOptions {
  readonly encrypted?: boolean;
  readonly provider?: string;
}

/**
 * IConfiguration provides application code with access to persistent settings and secrets.
 */
export interface IConfiguration {
  /**
   * Reads a string or primitive configuration value.
   */
  get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<string>>;

  /**
   * Reads a typed object or primitive configuration value.
   */
  getAs<T>(namespace: string, key: string, signal?: AbortSignal): Promise<Result<T>>;

  /**
   * Saves a configuration value.
   */
  set(
    namespace: string,
    key: string,
    value: string,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Saves a complex object as a configuration value.
   */
  setAs<T>(
    namespace: string,
    key: string,
    value: T,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Removes a configuration key from a namespace.
   */
  remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<boolean>>;
}
