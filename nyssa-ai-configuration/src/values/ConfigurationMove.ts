// Responsibility: Outcome returned when moving a configuration entry between providers.
// Public interface:
// `-- ConfigurationMove
//     +-- sourceDeleteError
//     `-- sourceValueRemains

/**
 * ConfigurationMove reports the result of migrating a configuration entry to a new provider.
 */
export interface ConfigurationMove {
  /** True when the source provider retained the old secret copy or source cleanup failed after publication. */
  readonly sourceValueRemains: boolean;
  /** Optional error detail when deleting the old source copy failed after the destination and local document were updated. */
  readonly sourceDeleteError?: string | undefined;
}
