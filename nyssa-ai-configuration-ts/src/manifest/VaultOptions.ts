// Responsibility: Data contract representing external vault configuration in the manifest or client options.
// Public interface:
// `-- VaultOptions
//     +-- credential
//     +-- endpoint
//     +-- kind
//     +-- name
//     +-- secrets
//     `-- settings

/**
 * VaultOptions configures an external key vault provider.
 */
export interface VaultOptions {
  /** Provider name referenced by configuration entries (e.g. 'corpVault'). */
  readonly name: string;
  /** Provider factory kind (e.g. 'azure-key-vault', 'in-memory'). */
  readonly kind: string;
  /** Optional vault service endpoint URL. */
  readonly endpoint?: string | undefined;
  /** Optional 'namespace/key' address holding the OAuth or bearer credential used to authenticate to this vault. */
  readonly credential?: string | undefined;
  /** Non-sensitive provider settings, normalized to {} when omitted. */
  readonly settings?: Readonly<Record<string, string>> | undefined;
  /** Sensitive provider secrets, normalized to {} when omitted. */
  readonly secrets?: Readonly<Record<string, string>> | undefined;
}
