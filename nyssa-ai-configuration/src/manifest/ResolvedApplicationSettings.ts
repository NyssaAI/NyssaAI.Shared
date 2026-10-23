// Responsibility: Data contract representing the settled configuration settings for one application.
// Public interface:
// `-- ResolvedApplicationSettings
//     +-- configurationDirectory
//     +-- keyVault
//     `-- lockTimeoutMs

import type { VaultOptions } from "./VaultOptions.js";

/**
 * ResolvedApplicationSettings holds the normalized directory, timeout, and vault settings for an application.
 */
export interface ResolvedApplicationSettings {
  readonly configurationDirectory: string;
  readonly lockTimeoutMs: number;
  readonly keyVault?: VaultOptions | undefined;
}
