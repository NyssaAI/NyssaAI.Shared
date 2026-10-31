// Responsibility: Data contract representing one application's configuration section in config-manifest.json.
// Public interface:
// `-- ApplicationManifestEntry
//     +-- configurationDirectory
//     +-- keyVault
//     +-- lockTimeout
//     `-- lockTimeoutMs

import type { VaultOptions } from "./VaultOptions.js";

/**
 * ApplicationManifestEntry represents one application's section inside the manifest.
 */
export interface ApplicationManifestEntry {
  readonly configurationDirectory?: string | undefined;
  readonly lockTimeout?: string | number | undefined;
  readonly lockTimeoutMs?: number | undefined;
  readonly keyVault?: VaultOptions | undefined;
}
