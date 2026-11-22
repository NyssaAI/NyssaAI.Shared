// Responsibility: Data contract representing the on-disk shape of ~/.nyssa-ai/config-manifest.json.
// Public interface:
// `-- ConfigurationManifest
//     +-- applications
//     +-- keyVaults
//     +-- namespaceRegistry
//     `-- version

import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";
import type { VaultManifestEntry } from "./VaultManifestEntry.js";

/**
 * ConfigurationManifest models the root JSON document at ~/.nyssa-ai/config-manifest.json.
 */
export interface ConfigurationManifest {
  readonly version: number;
  readonly keyVaults?: Readonly<Record<string, VaultManifestEntry>> | undefined;
  readonly namespaceRegistry?: Readonly<Record<string, string>> | undefined;
  readonly applications?: Readonly<Record<string, ApplicationManifestEntry>> | undefined;
}
