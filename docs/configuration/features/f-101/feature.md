---
id: F-101
phase: 1
status: planned
---

# F-101 Namespace Document Store & Core Value Operations

## Summary

Persists per-namespace JSON documents (`<configurationDirectory>/<namespace>.json`) and implements core key-value operations (`GetAsync`, `SetAsync`, `RemoveAsync`, and `ListAsync`) across literal plaintext entries and pluggable `IConfigurationProvider` backends.

## User Need

Application services need a fast, predictable key-value interface to read, write, list, and delete settings by `(namespace, key)` where each entry remembers its own storage provider and compactly serializes to disk.

## Use Cases

- [UC-001: Save and Read Setting with Provider Preservation](uc-001-save-and-read-setting.md)
- [UC-002: List Keys in a Namespace in Ordinal Order](uc-002-list-keys-in-ordinal-order.md)
- [UC-003: Remove a Setting and Report Remote Retention](uc-003-remove-setting-and-report-remote-retention.md)

## Scope

- `<namespace>.json` document reading and atomic, lock-guarded writing (`ConfigurationDocumentLocalFile` and `ConfigurationDocumentFactoryLocalFile`).
- Namespace and key identifier validation (rejecting empty/whitespace names, path traversal segments, or directory separators).
- Compact `ConfigurationValue` JSON serialization:
  - Always writes `"value"`.
  - Omits default metadata (`provider: null`, `encrypted: false`, `reauth_required: false`, `json: false`) while remaining backward-compatible when reading files with explicit default fields.
- Core `IConfiguration` / `ConfigurationStore` operations:
  - `GetAsync(namespace, key)`: reads `<namespace>.json`, resolves the entry via its recorded provider (or returns literal text), and throws/errors with `NotFound` if the namespace or key does not exist.
  - `SetAsync(namespace, key, value, provider?)`: uses plaintext (`provider: null`) for new keys when `provider` is omitted; preserves the existing provider on update when `provider` is omitted or matches the current provider; rejects changing an existing key's provider via `SetAsync` (requiring `MoveAsync`).
  - `RemoveAsync(namespace, key)`: invokes `EraseAsync` on the entry's recorded provider before removing the local entry, returning `ConfigurationRemoval(RemoteValueRemains)`; succeeds with `RemoteValueRemains = false` without contacting a provider when the local key is already absent; preserves the local entry if provider erasure fails operationally.
  - `ListAsync(namespace)`: returns keys sorted in deterministic ordinal (`StringComparer.Ordinal` / UTF-16 code unit) order, or an empty list if the namespace file does not exist.

## Out of Scope

- Typed DTO serialization, namespace-level replacement, and partial namespace merging (owned by `F-102`).
- Changing an existing entry's provider via `MoveAsync` (owned by `F-201`).
- Interactive OAuth flows and cloud vault adapters (owned by `F-202` and `F-203`).

## Inputs

- `namespace`, `key`, string `value`, and optional `provider` name.
- Registered `IConfigurationProvider` dictionary (`"aesLocal"`, `"oauth"`, and optional configured vault provider).

## Outputs

- Updated or deleted `<namespace>.json` entries on disk.
- Resolved string values from `GetAsync`, sorted key lists from `ListAsync`, and `ConfigurationRemoval` outcomes from `RemoveAsync`.

## Acceptance

- New keys default to plaintext when `provider` is omitted; updates without `provider` preserve the existing entry's provider.
- Calling `SetAsync` on an existing key with a different provider fails with a conflict/invalid-operation error directing the caller to `MoveAsync`.
- `RemoveAsync` reports `RemoteValueRemains = true` when a provider retains a soft-deleted or immutable remote copy, and `false` for local or fully deleted entries.
- `ListAsync` returns keys in strict ordinal order or an empty collection when the namespace is absent.
- Both `.NET` and `TypeScript/Node.js` implementations produce identical compact `<namespace>.json` files.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-001](../../features/f-001/feature.md)
- [F-003](../../features/f-003/feature.md)
