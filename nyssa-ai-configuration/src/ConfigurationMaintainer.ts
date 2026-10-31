// Responsibility: Public maintenance contract for migrating configuration providers and recovering interrupted writes.
// Public interface:
// `-- ConfigurationMaintainer
//     +-- move(namespace, key, destinationProvider, signal)
//     `-- recover(handle, signal)

import { type Result } from "./results/Result.js";
import type { ConfigurationMove } from "./values/ConfigurationMove.js";
import type { ConfigurationWriteRecoveryHandle } from "./recovery/ConfigurationWriteRecoveryHandle.js";

/**
 * ConfigurationMaintainer migrates configuration keys across providers and recovers interrupted multi-step writes.
 */
export interface ConfigurationMaintainer {
  /**
   * Migrates an existing configuration key to a new provider, writing to the destination first and withdrawing the source copy.
   */
  move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>>;

  /**
   * Replays an interrupted multi-step write using its portable recovery handle.
   */
  recover(handle: ConfigurationWriteRecoveryHandle, signal?: AbortSignal): Promise<Result<void>>;
}
