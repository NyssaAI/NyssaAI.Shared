// Responsibility: Represent the JSON data transfer record for a Bitwarden secret entry.
// Public interface:
// `-- BwsSecretEntry
//     +-- id
//     +-- key
//     +-- slug
//     `-- value

/**
 * Secret entry shape parsed from `bws secret list --output json`.
 */
export interface BwsSecretEntry {
  readonly id?: string;
  readonly key?: string;
  readonly slug?: string;
  readonly value?: string;
}
