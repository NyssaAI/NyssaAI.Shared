---
feature: F-204
id: UC-004
status: planned
---

# UC-004 Detect Conflicts or Require Manual Reconciliation

## Actor

Recovery Coordinator (`ConfigurationStore`, `IConfiguration.RecoverAsync`).

## Goal

Stop automatic recovery safely when concurrent changes have invalidated the prepared state (`Conflict`) or when a vault cannot provide the guarantees needed for automatic continuation (`ManualReconciliation`), preventing stale recovery handles from overwriting newer data.

## Starting Context

A multi-step operation failed mid-flight, or `RecoverAsync(handle)` is called after another process or operator modified local or remote settings.

## Entry Route

Evaluated during the initial failure handling of `MoveAsync` / `SetAsAsync` / `MergeSettingsAsync` and during `IConfiguration.RecoverAsync(handle)`.

## Trigger

A vault does not implement `ICloudKeyVaultRecovery`, backend request evidence expired, the vault's `StoreIdentity` changed, or local/remote configuration changed concurrently.

## Process

```text
Actor stops recovery on conflict or insufficient backend guarantees
+-- Starting context: mid-flight failure or RecoverAsync(handle) invocation
+-- Entry route: ConfigurationStore recovery evaluation
+-- Trigger: state divergence or missing recovery guarantees detected
+-- Branch A: Concurrent Change Detected -> Status = Conflict
|   +-- Case A1: Local <namespace>.json entry changed since the operation's Original snapshot
|   +-- Case A2: Configured vault StoreIdentity (account/tenant/store) changed since preparation
|   +-- Case A3: Vault ReconcileAsync or ExecuteAsync reports Conflict (remote secret was updated by someone else)
|   \-- Action:
|       +-- Stop immediately without executing remaining writes or overwriting <namespace>.json
|       \-- Return Status = Conflict and RecoveryHandle = null (handle is terminal)
+-- Branch B: Automatic Recovery Unavailable -> Status = ManualReconciliation
|   +-- Case B1: Vault adapter implements ICloudKeyVault but NOT ICloudKeyVaultRecovery
|   +-- Case B2: Vault ReconcileAsync returns Manual (e.g. backend request log expired)
|   \-- Action:
|       +-- Report exact per-step progress in Progress.Steps
|       \-- Return Status = ManualReconciliation and RecoveryHandle = null
\-- Success: System refuses unsafe continuation and gives operator exact step-by-step state
```

## Step Details

### Branch A & Branch B: Terminal Safety
- In both `Conflict` and `ManualReconciliation`, `RecoveryHandle` is `null` because automatic retry cannot safely resolve the state.
- `Progress.Steps` still reports the exact state of every property (`Applied`, `Unknown`, `Conflict`, `Manual`, `Pending`) without exposing secret payloads, so an operator or application knows which keys succeeded before stopping.

## Success

Unsafe automatic writes are prevented, no newer local or remote values are overwritten, and the caller receives a terminal `Conflict` or `ManualReconciliation` status with full step diagnostics.

## Failure Cases

- None (this use case defines the terminal safety guardrails for recovery).

## Consistency Check

- Unique coverage: Covers `Conflict` and `ManualReconciliation` terminal states and precondition enforcement (`StoreIdentity`, local snapshot comparison, and adapters without `ICloudKeyVaultRecovery`).
- Closest related use cases: `UC-001` and `UC-003` cover successful `Completed` recovery flows.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: concurrent local/remote edits or changed `StoreIdentity` yield `Conflict` with `RecoveryHandle = null`; non-recovery adapters yield `ManualReconciliation` with `RecoveryHandle = null`.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
