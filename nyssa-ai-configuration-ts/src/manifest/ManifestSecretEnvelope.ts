// Responsibility: Data contract representing an AES-256-GCM encrypted secret envelope in config-manifest.json.
// Public interface:
// `-- ManifestSecretEnvelope
//     +-- encrypted
//     +-- provider
//     `-- value

/**
 * ManifestSecretEnvelope models an AES-256-GCM sealed secret inside root keyVaults.<name>.secrets.
 */
export interface ManifestSecretEnvelope {
  readonly value: string;
  readonly encrypted: true;
  readonly provider?: "aesLocal" | undefined;
}
