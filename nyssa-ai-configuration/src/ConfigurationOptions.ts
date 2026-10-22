// Responsibility: Public options provided when initializing the configuration client.
// Public interface:
// `-- ConfigurationOptions
//     +-- applicationName
//     +-- browserLauncher
//     +-- clock
//     +-- configDirectory
//     +-- configManifest
//     +-- keyVault
//     +-- keyring
//     +-- lockTimeoutMs
//     +-- logger
//     +-- oauthClient
//     `-- vaultFactories

import type { IKeyring } from "./keyring/IKeyring.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";
import type { VaultOptions } from "./manifest/VaultOptions.js";
import type { IVaultProviderFactory } from "./vault/IVaultProviderFactory.js";
import type { IOAuthFlowClient } from "./oauth/IOAuthFlowClient.js";
import type { IBrowserLauncher } from "./oauth/IBrowserLauncher.js";

/**
 * ConfigurationOptions configures client initialization and testability seams.
 */
export interface ConfigurationOptions {
  /** The application name identifying the manifest section and keyring service (inferred when omitted). */
  readonly applicationName?: string | undefined;
  /** Explicit configuration directory (overrides default, recorded on first run). */
  readonly configDirectory?: string | undefined;
  /** Custom manifest file path (defaults to ~/.nyssa-ai/config-manifest.json). */
  readonly configManifest?: string | undefined;
  /** Developer-supplied default vault configuration (overridden whole if the manifest section defines keyVault). */
  readonly keyVault?: VaultOptions | undefined;
  /** Registered vault provider factories keyed by VaultOptions.kind. */
  readonly vaultFactories?: ReadonlyArray<IVaultProviderFactory> | undefined;
  /** Custom keyring implementation for master key custody. */
  readonly keyring?: IKeyring | undefined;
  /** Custom OAuth 2.0 flow client for authorization code and refresh token exchange. */
  readonly oauthClient?: IOAuthFlowClient | undefined;
  /** Custom browser launcher for interactive OAuth sign-in. */
  readonly browserLauncher?: IBrowserLauncher | undefined;
  /** Custom monotonic/wall clock returning epoch milliseconds (defaults to Date.now). */
  readonly clock?: (() => number) | undefined;
  /** Lock acquisition timeout in milliseconds (defaults to 10000ms). */
  readonly lockTimeoutMs?: number | undefined;
  /** Structured operational logger complying with TS-LOG. */
  readonly logger?: IConfigurationLogger | undefined;
}
