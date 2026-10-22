---
id: F-201
phase: 2
status: planned
---

# F-201 Provider Migration (`MoveAsync`)

## Summary

Migrates an existing configuration entry from its current storage provider to a different destination provider (`MoveAsync(namespace, key, toProvider?)`), writing the resolved value to the destination before erasing the source copy and reporting `ConfigurationMove.SourceWithdrawn`.

## User Need

Applications and operators frequently start with plaintext or local encryption (`"aesLocal"`) and later promote sensitive keys to a cloud vault (or return them to local storage) without losing the value or changing application read calls.

## Use Cases

- [UC-001: Move Setting to a New Provider](uc-001-move-setting-to-new-provider.md)
- [UC-002: Return Protected Setting to Default Plaintext Storage](uc-002-return-setting-to-default-plaintext-storage.md)
- [UC-003: Handle Retained or Failed Source Cleanup During Move](uc-003-handle-retained-or-failed-source-cleanup-during-move.md)

## Scope

- `MoveAsync(namespace, key, toProvider = null)`:
  - Reads and resolves the current entry from `<namespace>.json` through its current provider (preserving `"json": true` metadata for typed DTOs/strings).
  - Rejects moving a missing key (`NotFound`) or moving an entry to its existing provider (`Conflict` / invalid operation).
  - Interprets an omitted or `null` `toProvider` as returning the entry to default plaintext storage.
  - Writes the resolved value to `toProvider` first and publishes the updated `<namespace>.json` entry so the value is immediately usable at the destination.
  - Requests deletion (`EraseAsync`) from the previous provider after destination publication.
  - Returns `ConfigurationMove` with `SourceWithdrawn = true` when the source copy was local or confirmed deleted by the source provider, and `SourceWithdrawn = false` (plus a logged warning and optional `RecoveryHandle` from `F-204`) when the source provider retains a soft-deleted copy or source cleanup fails.

## Out of Scope

- Silent provider switching inside `SetAsync` or `SetAsAsync` (those methods enforce provider immutability and require `MoveAsync` for provider changes).
- Multi-step recovery token encryption and `RecoverAsync` orchestration (owned by `F-204`).

## Inputs

- `namespace`, `key`, and optional destination provider name (`toProvider`).

## Outputs

- Updated `<namespace>.json` entry bound to `toProvider`.
- `ConfigurationMove` result reporting `SourceWithdrawn` (and optional `RecoveryHandle` when source cleanup failed transiently and recovery is supported).

## Acceptance

- Moving an entry between plaintext, `"aesLocal"`, and a configured cloud vault preserves its exact value and JSON encoding metadata.
- Moving an entry to its current provider throws/fails.
- If source provider deletion reports `RemoteValueRemains = true` (e.g., soft delete) or fails after destination publication, the destination entry remains intact and `ConfigurationMove.SourceWithdrawn` is `false`.
- Both `.NET` and `TypeScript/Node.js` implementations support `MoveAsync` with identical semantics.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-101](../../features/f-101/feature.md)
- [F-102](../../features/f-102/feature.md)
