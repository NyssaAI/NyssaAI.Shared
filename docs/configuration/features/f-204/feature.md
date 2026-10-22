---
id: F-204
phase: 2
status: planned
---

# F-204 Guarded Two-Phase Recovery

## Summary

Captures structured, payload-free step progress (`ConfigurationOperationProgress`) when `MoveAsync`, namespace `SetAsAsync`, or `MergeSettingsAsync` fails or is canceled after remote mutations begin, issues HKDF-encrypted/authenticated `ConfigurationRecoveryHandle` tokens (`Export`/`Import`), and safely reconciles and completes unfinished work via `RecoverAsync(handle)` and `ICloudKeyVaultRecovery`.

## User Need

When a multi-step configuration operation (such as migrating a secret between providers or updating a namespace across multiple vault properties) fails mid-flight due to a dropped connection, process crash, or cancellation, applications and operators need accurate diagnostics of what committed remotely versus locally and a safe, idempotent way to finish or reconcile the operation without duplicating writes or leaking secrets.

## Use Cases

- [UC-001: Inspect Progress and Resume an Interrupted Namespace Write](uc-001-resume-interrupted-namespace-write.md)
- [UC-002: Export and Import a Recovery Handle Across Process Restarts](uc-002-export-and-import-recovery-handle.md)
- [UC-003: Complete Unfinished Source Cleanup After a Provider Move](uc-003-recover-unfinished-move-source-cleanup.md)
- [UC-004: Detect Conflicts or Require Manual Reconciliation](uc-004-detect-conflict-or-manual-reconciliation.md)

## Scope

- **Guarded Vault Recovery Port (`ICloudKeyVaultRecovery`):**
  - Optional interface implemented alongside `ICloudKeyVault` providing:
    - `StoreIdentity`: stable backend account/tenant/store identifier independent of registration name.
    - `PrepareAsync(operation)`: read-only capture of target state and atomic concurrency preconditions prior to mutation.
    - `ExecuteAsync(operation, preparation)`: atomic conditional execution and request-ID deduplication against frozen intent.
    - `ReconcileAsync(operation, preparation)`: read-only determination of whether an interrupted request was `Applied`, `Failed` (confirmed no effect and retry condition still valid), `Conflict`, `Retained`, or `Manual`.
- **Progress Receipt & Exception Contracts (`Recovery`):**
  - `ConfigurationOperationProgress`: reports `OperationId`, `Kind` (`Move`, `NamespaceReplace`, `NamespaceMerge`), `Namespace`, per-key `Steps` (`Pending`, `Applied`, `Failed`, `Unknown`, `Conflict`, `Retained`, `Manual`), local `Publication` status (`NotAttempted`, `Published`, `Failed`, `Unknown`), and overall `Recovery` status (`Completed`, `Unfinished`, `Conflict`, `ManualReconciliation`).
  - `ConfigurationOperationException` (deriving from `ResultException`) and `ConfigurationOperationCanceledException` (deriving from `OperationCanceledException`) carrying `Progress` and optional `RecoveryHandle`.
  - `ConfigurationMove.RecoveryHandle` and `ConfigurationMove.Progress` when destination publication succeeds but source withdrawal remains unfinished.
- **Protected Recovery Handle (`ConfigurationRecoveryHandle`):**
  - `Export()` and `Import(token)` for persisting recovery handles across application restarts.
  - Encrypted and authenticated using a purpose-specific subkey derived via HKDF-SHA256 from the application's 256-bit protection key, bound to `ApplicationName` and the canonical `configurationDirectory`.
  - Redacted `ToString()` formatting so exported tokens are never accidentally written to logs.
- **Recovery Execution (`IConfiguration.RecoverAsync(handle)`):**
  - Authenticates the handle against the current application, canonical configuration root, protection key, and provider `StoreIdentity`.
  - Reconciles `Unknown` steps via `ReconcileAsync`, reuses confirmed `Applied` outputs without re-serializing DTOs, executes remaining safe `Pending`/`Failed` steps, and publishes the intended `<namespace>.json` state (or completes source withdrawal for an already-published move).
  - Stops with `Conflict` if local configuration or remote store state diverged, or `ManualReconciliation` when a provider does not implement `ICloudKeyVaultRecovery` or backend evidence expired.

## Out of Scope

- Automatic background retry threads or on-disk write-ahead journals managed by the library.
- Adding recovery metadata fields to `<namespace>.json`.

## Inputs

- Multi-step operation intent from `MoveAsync`, namespace `SetAsAsync`, or `MergeSettingsAsync`.
- `ConfigurationRecoveryHandle` (in-memory or restored via `ConfigurationRecoveryHandle.Import(savedToken)`).
- Vault adapter implementing `ICloudKeyVaultRecovery`.

## Outputs

- `ConfigurationRecoveryOutcome` (`Status`, `Progress`, and updated `RecoveryHandle` if work remains unfinished).
- Reconciled remote vault state and published local `<namespace>.json` document.

## Acceptance

- Mid-flight failures or cancellations during `MoveAsync`, namespace `SetAsAsync`, or `MergeSettingsAsync` report exact step states (`Applied`, `Unknown`, `Pending`, `Failed`, `Conflict`, `Retained`, `Manual`) with zero secret payloads in `Progress`.
- `RecoverAsync` reconciles `Unknown` mutations, completes remaining steps without re-submitting already-applied writes, and publishes the intended local namespace document.
- Exported `ConfigurationRecoveryHandle` tokens survive process restarts via `Export()`/`Import()`, fail authentication if tampered with or used under a different application/key/directory, and redact themselves in `ToString()`.
- After a `MoveAsync` publishes its destination, `RecoverAsync` addresses only the recorded source cleanup and never deletes from the destination provider.
- Both `.NET` and `TypeScript/Node.js` implementations pass the full recovery use-case and unit test suites.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-003](../../features/f-003/feature.md)
- [F-102](../../features/f-102/feature.md)
- [F-201](../../features/f-201/feature.md)
- [F-203](../../features/f-203/feature.md)
