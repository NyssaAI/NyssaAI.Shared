// Responsibility: Cloud key vault pointer, operation, preparation, step effect, and recovery capability contracts.
// Public interface:
// +-- CloudKeyVaultAction
// +-- CloudKeyVaultOperation
// |   +-- action
// |   +-- key
// |   +-- namespace
// |   +-- operationId
// |   +-- pointer
// |   `-- value
// +-- CloudKeyVaultPreparation
// |   +-- precondition
// |   +-- storeIdentity
// |   `-- targetReference
// +-- CloudKeyVaultStepEffect
// |   +-- pointer
// |   `-- state
// +-- ConfigurationPointer
// |   +-- provider
// |   `-- value
// `-- ConfigurationStepState

/**
 * CloudKeyVaultAction classifies whether a remote step writes or removes a secret.
 */
export type CloudKeyVaultAction = "Write" | "Remove";

/**
 * ConfigurationStepState classifies the current state of one key step in a multi-step operation.
 */
export type ConfigurationStepState =
  | "Pending"
  | "Applied"
  | "Failed"
  | "Unknown"
  | "Conflict"
  | "Retained"
  | "Manual";

/**
 * ConfigurationPointer represents a local reference stored in <namespace>.json pointing to an external vault entry.
 */
export interface ConfigurationPointer {
  /** The registered vault provider name. */
  readonly provider: string;
  /** The versionless secret reference or item identifier in the vault. */
  readonly value: string;
}

/**
 * CloudKeyVaultOperation describes one remote vault write or removal request.
 */
export interface CloudKeyVaultOperation {
  /** Stable idempotency identifier for this vault operation. */
  readonly operationId: string;
  /** Whether the operation writes or removes a secret. */
  readonly action: CloudKeyVaultAction;
  /** Target configuration namespace. */
  readonly namespace: string;
  /** Target configuration key. */
  readonly key: string;
  /** Cleartext secret value when action is 'Write'. */
  readonly value?: string | undefined;
  /** Existing pointer when updating or removing an entry. */
  readonly pointer?: ConfigurationPointer | undefined;
}

/**
 * CloudKeyVaultPreparation captures the read-only target identity and precondition before a guarded mutation.
 */
export interface CloudKeyVaultPreparation {
  /** Stable backend store/tenant/account identity independent of registration name. */
  readonly storeIdentity: string;
  /** Secret-free target reference for diagnostic progress reporting. */
  readonly targetReference?: string | undefined;
  /** Optional optimistic concurrency precondition token. */
  readonly precondition?: string | undefined;
}

/**
 * CloudKeyVaultStepEffect reports the outcome of executing or reconciling a guarded cloud vault step.
 */
export interface CloudKeyVaultStepEffect {
  /** Resulting step state ('Applied', 'Failed', 'Conflict', 'Retained', or 'Manual'). */
  readonly state: ConfigurationStepState;
  /** Resulting ConfigurationPointer when a 'Write' step is 'Applied'. */
  readonly pointer?: ConfigurationPointer | undefined;
}
