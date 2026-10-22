// Responsibility: Public options provided when initializing the configuration client.
// Public interface:
// +-- ConfigurationOptions
// |   +-- applicationName
// |   +-- configDirectory
// |   +-- configManifest
// |   +-- keyring
// |   +-- lockTimeoutMs
// |   `-- logger

import type { IKeyring } from "./keyring/IKeyring.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";

/**
 * ConfigurationOptions configures client initialization.
 */
export interface ConfigurationOptions {
  /** The application name identifying the manifest section and keyring service. */
  readonly applicationName: string;
  /** Explicit configuration directory (overrides default, recorded on first run). */
  readonly configDirectory?: string;
  /** Custom manifest file path (defaults to ~/.nyssa-ai/config-manifest.json). */
  readonly configManifest?: string;
  /** Custom keyring implementation for master key custody. */
  readonly keyring?: IKeyring;
  /** Lock acquisition timeout in milliseconds (defaults to 10000ms). */
  readonly lockTimeoutMs?: number;
  /** Structured operational logger complying with TS-LOG. */
  readonly logger?: IConfigurationLogger;
}
