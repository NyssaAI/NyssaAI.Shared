// Responsibility: Public read-only contract for reading, deserializing, and listing configuration values.
// Public interface:
// `-- ConfigurationReader
//     +-- get(namespace, key, signal)
//     +-- getAs(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     `-- list(namespace, signal)

import { type Result } from "./results/Result.js";

/**
 * ConfigurationReader provides read-only access to settings, secrets, and OAuth access tokens.
 */
export interface ConfigurationReader {
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
   * Lists all keys in a namespace in strict ordinal code-unit order without decrypting values.
   */
  list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>>;
}
