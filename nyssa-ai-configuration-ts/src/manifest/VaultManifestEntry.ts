// Responsibility: Data contract representing one root keyVaults entry in config-manifest.json.
// Public interface:
// `-- VaultManifestEntry
//     +-- cacheTTLSeconds
//     +-- credential
//     +-- endpoint
//     +-- kind
//     +-- name
//     +-- secrets
//     `-- settings

import type { ManifestSecretEnvelope } from "./ManifestSecretEnvelope.js";

/**
 * VaultManifestEntry models one vault configuration entry under root keyVaults in config-manifest.json.
 */
export interface VaultManifestEntry {
  readonly name?: string | undefined;
  readonly kind: string;
  readonly endpoint?: string | undefined;
  readonly credential?: string | undefined;
  readonly cacheTTLSeconds?: number | undefined;
  readonly settings?: Readonly<Record<string, string>> | undefined;
  readonly secrets?: Readonly<Record<string, ManifestSecretEnvelope>> | undefined;
}
