// Responsibility: Define pure data transfer structures for the Bitwarden Secrets Manager CLI JSON output.
// Public interface:
// `-- BwsSecretEntry
//     +-- id
//     +-- key
//     +-- slug
//     `-- value

/**
 * Secret entry shape parsed from `bws secret list --output json` and `bws secret get --output json`.
 */
export interface BwsSecretEntry {
  readonly id?: string;
  readonly key?: string;
  readonly slug?: string;
  readonly value?: string;
}
