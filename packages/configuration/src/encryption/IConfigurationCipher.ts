// Responsibility: Define the contract for ciphers sealing and opening configuration values.
// Public interface:
// +-- IConfigurationCipher
// |   +-- open(value, namespace, key)
// |   `-- seal(value, namespace, key)

import { type Result } from "../results/Result.js";

/**
 * IConfigurationCipher encrypts and decrypts configuration entries with authenticated metadata.
 */
export interface IConfigurationCipher {
  /**
   * Seals a string value with associated namespace and key.
   */
  seal(value: string, namespace: string, key: string): Result<string>;

  /**
   * Opens an encrypted value, verifying its authenticity and associated namespace and key.
   */
  open(value: string, namespace: string, key: string): Result<string>;
}
