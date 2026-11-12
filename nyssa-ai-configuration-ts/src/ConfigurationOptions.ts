// Responsibility: Public options provided when initializing the configuration client.
// Public interface:
// `-- ConfigurationOptions
//     +-- applicationName
//     +-- browserLauncher
//     +-- clock
//     +-- configDirectory
//     +-- configManifest
//     +-- keyVault
//     +-- keyVaults
//     +-- keyring
//     +-- lockTimeoutMs
//     +-- logger
//     +-- manifestKeyring
//     +-- oauthClient
//     +-- secretCache
//     +-- systemdCredentialName
//     `-- vaultFactories

import type { IKeyring } from "./keyring/IKeyring.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";
import type { VaultOptions } from "./manifest/VaultOptions.js";
import type { IVaultProviderFactory } from "./vault/IVaultProviderFactory.js";
import type { SecretCache } from "./vault/SecretCache.js";
import type { IOAuthFlowClient } from "./oauth/IOAuthFlowClient.js";
import type { IBrowserLauncher } from "./oauth/IBrowserLauncher.js";

/**
 * ConfigurationOptions configures client initialization and testability seams.
 */
export interface ConfigurationOptions {
  /** The application name identifying the manifest section and keyring service (inferred when omitted). */
  readonly applicationName?: string | undefined;
  /** Explicit configuration directory (overrides default of <cwd>/.nyssa-ai/config, recorded on first run). */
  readonly configDirectory?: string | undefined;
  /** Custom manifest file path (defaults to ~/.nyssa-ai/config-manifest.json). */
  readonly configManifest?: string | undefined;
  /** Developer-supplied single default vault configuration (overridden whole if root manifest keyVaults defines a matching name). */
  readonly keyVault?: VaultOptions | undefined;
  /** Additional developer-supplied named vault configurations layered beneath root manifest keyVaults. */
  readonly keyVaults?: ReadonlyArray<VaultOptions> | undefined;
  /** Registered vault provider factories keyed by VaultOptions.kind. */
  readonly vaultFactories?: ReadonlyArray<IVaultProviderFactory> | undefined;
  /** Custom keyring implementation for application master key custody. */
  readonly keyring?: IKeyring | undefined;
  /** Optional custom keyring implementation dedicated to '_manifest' master key custody. */
  readonly manifestKeyring?: IKeyring | undefined;
  /** Optional custom secret cache store (e.g. OS keyring cache for CLI processes) used by VaultProviderCaching. */
  readonly secretCache?: SecretCache | undefined;
  /** Name of the systemd credential file in CREDENTIALS_DIRECTORY (defaults to applicationName or master-key). */
  readonly systemdCredentialName?: string | undefined;
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
