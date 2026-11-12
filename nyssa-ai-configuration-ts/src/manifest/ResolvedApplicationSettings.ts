// Responsibility: Data contract representing the settled configuration settings for one application.
// Public interface:
// `-- ResolvedApplicationSettings
//     +-- configurationDirectory
//     +-- keyVaults
//     `-- lockTimeoutMs

import type { VaultManifestEntry } from "./VaultManifestEntry.js";

/**
 * ResolvedApplicationSettings holds the normalized directory, timeout, and root keyVaults for an application.
 */
export interface ResolvedApplicationSettings {
  readonly configurationDirectory: string;
  readonly lockTimeoutMs: number;
  readonly keyVaults?: Readonly<Record<string, VaultManifestEntry>> | undefined;
}
