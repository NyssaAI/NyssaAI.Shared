// Responsibility: Data contracts representing the on-disk shape of ~/.nyssa-ai/config-manifest.json.
// Public interface:
// +-- ApplicationManifestEntry
// |   +-- configurationDirectory
// |   +-- keyVault
// |   `-- lockTimeout
// +-- ConfigurationManifest
// |   +-- applications
// |   `-- version
// `-- VaultOptions
//     +-- endpoint
//     +-- kind
//     +-- name
//     +-- secrets
//     `-- settings

export interface VaultOptions {
  readonly name: string;
  readonly kind: string;
  readonly endpoint?: string;
  readonly settings?: Readonly<Record<string, string>>;
  readonly secrets?: Readonly<Record<string, string>>;
}

export interface ApplicationManifestEntry {
  readonly configurationDirectory?: string | undefined;
  readonly lockTimeout?: string | undefined;
  readonly keyVault?: VaultOptions | undefined;
}

export interface ConfigurationManifest {
  readonly version: number;
  readonly applications?: Readonly<Record<string, ApplicationManifestEntry>>;
}
