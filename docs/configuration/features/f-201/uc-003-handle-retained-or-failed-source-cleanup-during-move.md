---
feature: F-201
id: UC-003
status: planned
---

# UC-003 Handle Retained or Failed Source Cleanup During Move

## Actor

Application Service (`IConfiguration.MoveAsync`).

## Goal

Keep the moved setting working at its new destination provider even when the old source vault retains a soft-deleted copy or fails to delete the old secret, clearly reporting `SourceWithdrawn = false` and supplying a recovery handle when source deletion can be retried.

## Starting Context

A setting is being moved from a remote cloud key vault (`"mailVault"`) to another provider (such as `"aesLocal"` or plaintext).

## Entry Route

Called via `var moved = await configuration.MoveAsync("mail", "password", "aesLocal", cancellationToken)`.

## Trigger

`MoveAsync` finishes writing and publishing the secret at the destination provider, and then attempts to erase the secret from the source cloud vault.

## Process

```text
Actor handles source cleanup retention or failure after move destination is published
+-- Starting context: destination write and <namespace>.json publication have succeeded
+-- Entry route: source erasure phase of IConfiguration.MoveAsync
+-- Trigger: source vault EraseAsync is called for the previous pointer
+-- Branch A: Source vault soft-deletes or retains a copy (RemoteValueRemains == true)
|   +-- Source vault confirms deletion request but keeps a recoverable copy
|   +-- Log warning that source copy was retained by provider
|   \-- Return ConfigurationMove { SourceWithdrawn = false, RecoveryHandle = null }
|       (Retention is terminal; retrying cannot purge a soft-deleted secret)
+-- Branch B: Source vault deletion fails operationally (e.g. network drop or timeout)
|   +-- Destination entry in <namespace>.json remains active and usable
|   +-- Log warning that source withdrawal did not complete
|   +-- Source vault implements ICloudKeyVaultRecovery (F-204):
|   |   \-- Return ConfigurationMove { SourceWithdrawn = false, Progress = ..., RecoveryHandle = handle }
|   \-- Source vault does not implement recovery:
|       \-- Return ConfigurationMove { SourceWithdrawn = false, Progress = (ManualReconciliation), RecoveryHandle = null }
\-- Success: Caller can immediately read the moved setting from its new provider and inspect SourceWithdrawn
```

## Step Details

### Branch A vs. Branch B
- Once the new destination is published to `<namespace>.json`, `MoveAsync` does not throw just because cleaning up the old source copy had an issue—throwing would hide the fact that the move already took effect at the destination.
- Instead, `MoveAsync` returns normally with `moved.SourceWithdrawn = false`:
  - If the source vault soft-deleted the secret (`RemoteValueRemains = true`), `Progress.Recovery` is `Completed` and `RecoveryHandle` is `null` because soft-delete retention is a normal terminal state of that vault.
  - If the source vault failed to answer during erasure, `Progress.Recovery` is `Unfinished` and `moved.RecoveryHandle` allows the caller to finish erasing the old source secret later via `RecoverAsync` (`F-204 UC-003`).

## Success

The moved setting is immediately readable from its new provider, and `moved.SourceWithdrawn = false` (along with `moved.Progress` and `moved.RecoveryHandle`) tells the caller whether the old source copy was soft-deleted or needs retry cleanup.

## Failure Cases

- If the *destination* write or local `<namespace>.json` publication fails before Step 3 completes, `MoveAsync` throws `ResultException` / `ConfigurationOperationException` and leaves the entry at its original source provider.

## Consistency Check

- Unique coverage: Covers `SourceWithdrawn = false` outcomes during `MoveAsync` (both terminal soft-delete retention and retryable source cleanup failures).
- Closest related use cases: `UC-001` covers the clean move path where `SourceWithdrawn = true`; `F-204 UC-003` covers calling `RecoverAsync` with the returned `RecoveryHandle`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: destination remains active, `SourceWithdrawn` is `false`, a warning is logged, and `RecoveryHandle` is populated when source cleanup failed transiently on a recovery-capable vault.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
