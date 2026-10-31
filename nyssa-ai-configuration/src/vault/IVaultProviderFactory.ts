// Responsibility: Factory contract for instantiating external cloud key vault providers from manifest settings.
// Public interface:
// `-- IVaultProviderFactory
//     +-- authorization(options)
//     +-- create(options, getCredential)
//     `-- kind

import { type Result } from "../results/Result.js";
import type { VaultOptions } from "../manifest/VaultOptions.js";
import type { IVaultProvider } from "./IVaultProvider.js";
import type { OAuthOptions } from "../oauth/OAuthOptions.js";

/**
 * IVaultProviderFactory creates an IVaultProvider for a given vault kind.
 */
export interface IVaultProviderFactory {
  /** The vault kind string matched against VaultOptions.kind (e.g. 'azure-key-vault', 'in-memory'). */
  readonly kind: string;

  /**
   * Creates a configured IVaultProvider instance.
   */
  create(
    options: VaultOptions,
    getCredential: (signal?: AbortSignal) => Promise<Result<string>>
  ): Result<IVaultProvider>;

  /**
   * Optional OAuth 2.0 authorization options used when acquiring credentials via authorizeProvider.
   */
  authorization?(options: VaultOptions): Result<OAuthOptions | undefined>;
}
