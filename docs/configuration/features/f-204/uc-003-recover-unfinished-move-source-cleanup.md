---
feature: F-204
id: UC-003
status: planned
---

# UC-003 Complete Unfinished Source Cleanup After a Provider Move

## Actor

Application Service (`IConfiguration.MoveAsync`, `IConfiguration.RecoverAsync`).

## Goal

Finish deleting the old copy of a secret from its previous source vault when `MoveAsync` succeeded at writing and publishing the new destination provider, without ever deleting the secret from its new destination.

## Starting Context

`MoveAsync("mail", "password", "aesLocal")` wrote and published the secret at `"aesLocal"`, but erasing the old copy from the source cloud vault failed (e.g., network drop), returning `moved.SourceWithdrawn = false` and a non-null `moved.RecoveryHandle` (`F-201 UC-003`).

## Entry Route

Called via `var outcome = await configuration.RecoverAsync(moved.RecoveryHandle, cancellationToken)`.

## Trigger

The application decides to retry cleaning up the old source secret using the `RecoveryHandle` returned by `MoveAsync`.

## Process

```text
Actor recovers unfinished source cleanup after a published move
+-- Starting context: move published destination in <namespace>.json; source Erase remained Unknown/Failed
+-- Entry route: IConfiguration.RecoverAsync(moved.RecoveryHandle)
+-- Trigger: caller retries source cleanup
+-- Step 1: Authenticate handle and verify local state
|   +-- Verify <namespace>.json still holds the published destination entry from the move
|   \-- Local entry was modified or moved again -> Stop with Status = Conflict
+-- Step 2: Reconcile and execute ONLY the recorded source deletion step
|   +-- Never touch or delete from the current destination provider ("aesLocal")
|   +-- Call sourceVault.ReconcileAsync(deleteOperation, preparation) if state was Unknown:
|   |   +-- Source vault confirms deletion already took effect -> Step is Applied
|   |   +-- Source vault reports soft-delete retention -> Step is Retained (terminal)
|   |   \-- Source vault confirms secret is still untouched -> Call sourceVault.ExecuteAsync(deleteOperation)
+-- Step 3: Report final recovery outcome
|   +-- Source deletion completed (or confirmed Retained) -> Status = Completed, RecoveryHandle = null
|   \-- Source vault still unreachable -> Status = Unfinished, return updated RecoveryHandle
\-- Success: Old source copy is withdrawn (or confirmed retained) while destination entry stays intact
```

## Step Details

### Step 2: Reconcile and execute ONLY the recorded source deletion step
- Because `Publication` is already `Published` for this move, `RecoverAsync` knows the destination entry in `<namespace>.json` is live.
- It operates strictly against the recorded source provider and prepared source pointer captured before the move—it **never** calls `EraseAsync` on the entry's current provider.

## Success

The old secret is erased from the source vault, the new destination entry in `<namespace>.json` remains untouched, and `outcome.Status` is `Completed`.

## Failure Cases

- `Conflict`: The local entry or source vault secret was modified by another operation after the move completed.
- `Unfinished`: The source vault is still unreachable; `outcome.RecoveryHandle` is returned for a later retry.

## Consistency Check

- Unique coverage: Covers `RecoverAsync` for post-publication source cleanup after `MoveAsync`.
- Closest related use cases: `F-201 UC-003` produces the `MoveAsync` outcome with `SourceWithdrawn = false` and `RecoveryHandle`; `UC-001` covers recovering unpublished namespace writes.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: `RecoverAsync` deletes only from the original source provider, leaves the destination provider intact, and transitions `Status` to `Completed`.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
