// Responsibility: Outcome returned when removing a configuration entry.
// Public interface:
// `-- ConfigurationRemoval
//     `-- remoteValueRemains

/**
 * ConfigurationRemoval reports whether a remote copy of a removed value was retained by the provider.
 */
export interface ConfigurationRemoval {
  /** True when the remote vault retained the secret (e.g. soft-delete or retention policy). */
  readonly remoteValueRemains: boolean;
}
