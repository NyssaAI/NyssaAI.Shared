// Responsibility: Data contract representing the on-disk shape of ~/.nyssa-ai/config-manifest.json.
// Public interface:
// `-- ConfigurationManifest
//     +-- applications
//     `-- version

import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";

/**
 * ConfigurationManifest models the root JSON document at ~/.nyssa-ai/config-manifest.json.
 */
export interface ConfigurationManifest {
  readonly version: number;
  readonly applications?: Readonly<Record<string, ApplicationManifestEntry>> | undefined;
}
