---
feature: F-204
id: UC-001
status: planned
---

# UC-001 Inspect Progress and Resume an Interrupted Namespace Write

## Actor

Application Service (`IConfiguration.SetAsAsync`, `MergeSettingsAsync`, `RecoverAsync`).

## Goal

When a multi-property namespace write fails mid-flight (for example, after one cloud vault secret commits, a second vault call loses its network response, and a third never starts), inspect the payload-free progress receipt and call `RecoverAsync(handle)` to safely reconcile and finish the write without repeating DTO serialization.

## Starting Context

A namespace (`"mail"`) has multiple properties backed by providers (including a recovery-capable cloud vault implementing `ICloudKeyVaultRecovery`). The application calls `SetAsAsync("mail", mailSettings)` or `MergeSettingsAsync("mail", mailSettings)`.

## Entry Route

1. Initial attempt throws `ConfigurationOperationException` (or `ConfigurationOperationCanceledException` on cancellation) carrying `failure.Progress` and `failure.RecoveryHandle`.
2. Resumption is called via `var outcome = await configuration.RecoverAsync(handle, cancellationToken)`.

## Trigger

A remote provider write or local `<namespace>.json` publication fails or is canceled after work has begun.

## Process

```text
Actor inspects mid-flight failure and resumes namespace write via RecoverAsync
+-- Starting context: multi-property namespace SetAsAsync or MergeSettingsAsync started
+-- Entry route: catch (ConfigurationOperationException failure) -> configuration.RecoverAsync(handle)
+-- Trigger: remote mutation fails/drops connection or local publication fails
+-- Phase A: Original Attempt Captures Step Progress
|   +-- PrepareAsync captures atomic concurrency conditions for each step before any mutation
|   +-- Step A succeeds -> State: Applied (pointer recorded)
|   +-- Step B dispatches to vault, but connection drops -> State: Unknown
|   +-- Step C never starts -> State: Pending
|   \-- Throw ConfigurationOperationException with:
|       +-- Progress (Steps: [A: Applied, B: Unknown, C: Pending], Publication: NotAttempted, Recovery: Unfinished)
|       \-- RecoveryHandle (encrypted frozen intent + confirmed outputs)
+-- Phase B: Application Calls RecoverAsync(handle)
|   +-- Authenticate handle against application name, canonical config directory, and protection key
|   +-- Verify local <namespace>.json has not diverged from Original snapshot
|   +-- Verify vault StoreIdentity matches the prepared identity
|   +-- Reconcile Unknown steps (Step B):
|   |   +-- Call vault.ReconcileAsync(operationB, preparationB)
|   |   +-- Vault confirms B already committed -> Mark B as Applied with its original pointer
|   |   \-- Vault confirms B had no effect and condition holds -> Execute B now
|   +-- Reuse confirmed Applied steps (Step A is NOT re-sent)
|   +-- Execute remaining Pending steps (Step C) via vault.ExecuteAsync
|   \-- Publish intended <namespace>.json atomically
\-- Success: ConfigurationRecoveryOutcome returned with Status = Completed and RecoveryHandle = null
```

## Step Details

### Phase A & Phase B: Idempotent Continuation Without Resubmitting the DTO
- `Progress` contains zero secret values or plaintext settings—only step keys, provider names, stable request IDs, step states, and non-secret target references, making it safe to display or log to an operator.
- `RecoverAsync(handle)` never asks the caller to pass `mailSettings` again. It uses the frozen prepared intent inside the handle, skips steps already confirmed `Applied`, reconciles `Unknown` steps via `ICloudKeyVaultRecovery.ReconcileAsync`, executes remaining `Pending`/`Failed` steps with atomic preconditions, and publishes `<namespace>.json`.

## Success

All remote vault writes are reconciled and completed without duplicate mutations, `<namespace>.json` is published, and `outcome.Status` is `ConfigurationRecoveryStatus.Completed`.

## Failure Cases

- `Conflict`: Another process modified `<namespace>.json` or the remote vault entry after the original attempt started (see `UC-004`).
- `ManualReconciliation`: A provider does not implement `ICloudKeyVaultRecovery` or backend reconciliation evidence has expired (see `UC-004`).
- `Unfinished`: A transient error occurred again during recovery; `outcome.RecoveryHandle` returns an updated handle with the latest progress for another retry.

## Consistency Check

- Unique coverage: Covers mid-flight failure diagnostics (`ConfigurationOperationException`) and resuming an interrupted namespace `SetAsAsync` / `MergeSettingsAsync` via `RecoverAsync`.
- Closest related use cases: `UC-002` exports/imports the handle across restarts; `UC-003` recovers unfinished source cleanup after `MoveAsync`; `UC-004` covers `Conflict` and `ManualReconciliation` outcomes.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: `Applied` steps are not re-executed, `Unknown` steps are reconciled via `ReconcileAsync`, `Pending` steps are executed, and `<namespace>.json` is published.
- Failure cases produce clear guidance with zero secret leakage in `Progress`.
- The consistency check shows no unresolved duplicate coverage or contradictions.
