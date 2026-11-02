// Responsibility: Optional guarded two-phase recovery capability implemented by cloud key vault providers.
// Public interface:
// `-- CloudKeyVaultRecovery
//     +-- execute(operation, preparation, signal)
//     +-- prepare(operation, signal)
//     +-- reconcile(operation, preparation, signal)
//     `-- storeIdentity

import { type Result } from "@nyssa-ai/result";
import type {
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  CloudKeyVaultStepEffect
} from "./Types.js";

/**
 * CloudKeyVaultRecovery provides guarded two-phase prepare, execute, and reconcile operations for F-204 recovery.
 */
export interface CloudKeyVaultRecovery {
  /** Stable backend store/tenant/account identity independent of registration name. */
  readonly storeIdentity: string;

  /**
   * Captures target identity and concurrency preconditions without mutating remote state.
   */
  prepare(
    operation: CloudKeyVaultOperation,
    signal?: AbortSignal
  ): Result<CloudKeyVaultPreparation> | Promise<Result<CloudKeyVaultPreparation>>;

  /**
   * Executes the prepared mutation once, deduplicating repeat operationIds.
   */
  execute(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation,
    signal?: AbortSignal
  ): Result<CloudKeyVaultStepEffect> | Promise<Result<CloudKeyVaultStepEffect>>;

  /**
   * Determines whether an interrupted operation was Applied, Failed, Conflict, Retained, or Manual without mutating state.
   */
  reconcile(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation,
    signal?: AbortSignal
  ): Result<CloudKeyVaultStepEffect> | Promise<Result<CloudKeyVaultStepEffect>>;
}
