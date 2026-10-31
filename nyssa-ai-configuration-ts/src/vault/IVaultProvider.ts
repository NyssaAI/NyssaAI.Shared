// Responsibility: Contract for external cloud key vault providers storing and retrieving secrets.
// Public interface:
// `-- IVaultProvider
//     +-- deleteSecret(secretName, signal)
//     +-- getSecret(secretName, signal)
//     +-- name
//     `-- setSecret(secretName, secretValue, signal)

import { type Result } from "../results/Result.js";

/**
 * IVaultProvider adapts an external secret manager (such as Azure Key Vault or HashiCorp Vault).
 */
export interface IVaultProvider {
  /** Unique provider name matching the manifest keyVault.name. */
  readonly name: string;

  /**
   * Retrieves a secret value by its versionless identifier ('namespace/key').
   */
  getSecret(secretName: string, signal?: AbortSignal): Promise<Result<string>>;

  /**
   * Stores a secret value under its versionless identifier ('namespace/key').
   */
  setSecret(secretName: string, secretValue: string, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Deletes a secret from the vault, reporting whether the secret was deleted or retained.
   */
  deleteSecret(
    secretName: string,
    signal?: AbortSignal
  ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>>;
}
