// Responsibility: Outcome returned when moving a configuration entry between providers.
// Public interface:
// `-- ConfigurationMove
//     +-- progress
//     +-- recoveryHandle
//     +-- sourceDeleteError
//     `-- sourceValueRemains

import type { ConfigurationRecoveryHandle } from "../recovery/ConfigurationRecoveryHandle.js";
import type { ConfigurationOperationProgress } from "../recovery/Types.js";

/**
 * ConfigurationMove reports the result of migrating a configuration entry to a new provider.
 */
export interface ConfigurationMove {
  /** True when the source provider retained the old secret copy or source cleanup failed after publication. */
  readonly sourceValueRemains: boolean;
  /** Optional error detail when deleting the old source copy failed after the destination and local document were updated. */
  readonly sourceDeleteError?: string | undefined;
  /** Optional operation progress receipt when source cleanup failed or remained unfinished. */
  readonly progress?: ConfigurationOperationProgress | undefined;
  /** Optional recovery handle when source cleanup can be resumed via recover(). */
  readonly recoveryHandle?: ConfigurationRecoveryHandle | undefined;
}
