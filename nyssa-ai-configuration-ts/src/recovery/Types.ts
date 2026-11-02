// Responsibility: Define payload-free operation progress receipts, recovery outcomes, and encrypted replay state records.
// Public interface:
// +-- ConfigurationOperationKind
// +-- ConfigurationOperationProgress
// |   +-- kind
// |   +-- namespace
// |   +-- operationId
// |   +-- publication
// |   +-- recovery
// |   `-- steps
// +-- ConfigurationPublicationState
// +-- ConfigurationRecoveryOutcome
// |   +-- progress
// |   +-- recoveryHandle
// |   `-- status
// +-- ConfigurationRecoveryState
// |   +-- conflicted
// |   +-- kind
// |   +-- namespace
// |   +-- operationId
// |   +-- original
// |   +-- proposed
// |   +-- publication
// |   +-- steps
// |   `-- version
// +-- ConfigurationRecoveryStatus
// +-- ConfigurationRecoveryStep
// |   +-- isJsonValue
// |   +-- local
// |   +-- operation
// |   +-- output
// |   +-- preparation
// |   +-- provider
// |   +-- source
// |   +-- state
// |   `-- targetReference
// `-- ConfigurationStepProgress
//     +-- action
//     +-- key
//     +-- operationId
//     +-- provider
//     +-- state
//     `-- targetReference

import type {
  CloudKeyVaultAction,
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  ConfigurationStepState
} from "../vault/Types.js";
import type { ConfigurationRecoveryHandle } from "./ConfigurationRecoveryHandle.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";

/**
 * ConfigurationOperationKind classifies the multi-step configuration operation.
 */
export type ConfigurationOperationKind = "Move" | "Replace" | "Merge";

/**
 * ConfigurationPublicationState classifies the local <namespace>.json publication phase.
 */
export type ConfigurationPublicationState = "NotAttempted" | "Published" | "Failed" | "Unknown";

/**
 * ConfigurationRecoveryStatus classifies whether an operation completed, can be resumed, conflicted, or needs manual reconciliation.
 */
export type ConfigurationRecoveryStatus =
  | "Completed"
  | "Unfinished"
  | "Conflict"
  | "ManualReconciliation";

/**
 * ConfigurationStepProgress is a payload-free diagnostic receipt for a single key step in a multi-step operation.
 */
export interface ConfigurationStepProgress {
  readonly operationId: string;
  readonly key: string;
  readonly provider: string;
  readonly action: CloudKeyVaultAction;
  readonly state: ConfigurationStepState;
  readonly targetReference?: string | undefined;
}

/**
 * ConfigurationOperationProgress is a payload-free progress report for a Move, Replace, or Merge operation.
 */
export interface ConfigurationOperationProgress {
  readonly operationId: string;
  readonly namespace: string;
  readonly kind: ConfigurationOperationKind;
  readonly publication: ConfigurationPublicationState;
  readonly recovery: ConfigurationRecoveryStatus;
  readonly steps: readonly ConfigurationStepProgress[];
}

/**
 * ConfigurationRecoveryOutcome is returned by recover(handle) describing final or remaining work.
 */
export interface ConfigurationRecoveryOutcome {
  readonly status: ConfigurationRecoveryStatus;
  readonly progress: ConfigurationOperationProgress;
  readonly recoveryHandle?: ConfigurationRecoveryHandle | undefined;
}

/**
 * ConfigurationRecoveryStep is an internal replay step stored inside an encrypted ConfigurationRecoveryState.
 */
export interface ConfigurationRecoveryStep {
  readonly provider: string;
  readonly operation: CloudKeyVaultOperation;
  readonly local: boolean;
  readonly isJsonValue: boolean;
  readonly preparation?: CloudKeyVaultPreparation | undefined;
  readonly targetReference?: string | undefined;
  readonly output?: ConfigurationValue | undefined;
  readonly source?: ConfigurationValue | undefined;
  readonly state: ConfigurationStepState;
}

/**
 * ConfigurationRecoveryState is the encrypted replay state sealed inside a ConfigurationRecoveryHandle.
 */
export interface ConfigurationRecoveryState {
  readonly operationId: string;
  readonly namespace: string;
  readonly kind: ConfigurationOperationKind;
  readonly original: Readonly<Record<string, ConfigurationValue>>;
  readonly proposed: Readonly<Record<string, ConfigurationValue>>;
  readonly steps: readonly ConfigurationRecoveryStep[];
  readonly publication: ConfigurationPublicationState;
  readonly conflicted: boolean;
  readonly version: number;
}
