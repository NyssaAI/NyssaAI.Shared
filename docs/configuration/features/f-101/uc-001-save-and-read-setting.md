---
feature: F-101
id: UC-001
status: planned
---

# UC-001 Save and Read Setting with Provider Preservation

## Actor

Application Service (`IConfiguration.SetAsync`, `IConfiguration.GetAsync`, `ConfigurationStore`).

## Goal

Save and read individual string configuration settings by `(namespace, key)`, defaulting new keys to plaintext while preserving an existing key's storage provider on updates and rejecting accidental provider changes.

## Starting Context

The configuration store is initialized. The caller wants to create, update, or read a setting in `<namespace>.json`.

## Entry Route

Called via `await configuration.SetAsync(namespace, key, value, provider)` and `await configuration.GetAsync(namespace, key)`.

## Trigger

An application reads or writes a named configuration key.

## Process

```text
Actor saves or reads a single configuration key
+-- Starting context: namespace and key supplied
+-- Entry route: IConfiguration.SetAsync or GetAsync
+-- Trigger: application reads or updates a setting
+-- Phase A: Saving a Setting (SetAsync)
|   +-- Validate namespace and key names (reject blank, whitespace, or path separators)
|   +-- Acquire <namespace>.json lock and read current entries
|   +-- Determine target provider:
|   |   +-- Key is NEW:
|   |   |   +-- provider omitted or null -> Use plaintext (literal)
|   |   |   \-- provider named (e.g. "aesLocal" or vault) -> Use named provider
|   |   \-- Key ALREADY EXISTS:
|   |       +-- provider omitted, null, or matches existing provider -> Keep existing provider
|   |       \-- provider differs from existing provider -> Fail with Conflict (must use MoveAsync)
|   +-- Write value through target provider (or wrap literal value)
|   \-- Publish compact entry to <namespace>.json atomically
+-- Phase B: Reading a Setting (GetAsync)
|   +-- Validate namespace and key names
|   +-- Read <namespace>.json under lock
|   +-- Key or namespace does not exist -> Fail with NotFound
|   +-- Entry has no provider (literal) -> Return entry.Value directly
|   \-- Entry names a provider -> Resolve and return value via provider.ReadAsync
\-- Success: Setting is persisted or returned as a string
```

## Step Details

### Phase A: Saving a Setting (`SetAsync`)
- **Provider Immutability on Update:** Once a key is saved under `"aesLocal"` or a cloud vault, later `SetAsync(namespace, key, newValue)` calls that omit `provider` automatically keep encrypting or writing to that vault. Passing a *different* provider to `SetAsync` is rejected with `ErrorCategory.Conflict` so a secret is never accidentally downgraded or moved without calling `MoveAsync` (`F-201`).
- **Compact JSON Format:** Plaintext entries are written simply as `{"value": "..."}`, omitting default `provider`, `encrypted`, `reauth_required`, and `json` fields.

## Success

The setting is saved in `<namespace>.json` with its intended provider (or read back accurately through that provider).

## Failure Cases

- `Validation`: `namespace` or `key` is blank, contains path traversal segments (`..`, `/`, `\`), or names an unregistered provider.
- `Conflict`: `SetAsync` was called on an existing key with a different provider than the one already recorded for that key.
- `NotFound`: `GetAsync` was called for a key or namespace that does not exist.

## Consistency Check

- Unique coverage: Covers individual key `SetAsync` and `GetAsync`, compact `ConfigurationValue` serialization, and provider immutability on update.
- Closest related use cases: `UC-002` lists keys in a namespace; `UC-003` removes a key; `F-201 UC-001` changes an existing key's provider via `MoveAsync`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: new keys default to plaintext, updates preserve the existing provider, and changing providers on `SetAsync` fails with `Conflict`.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
