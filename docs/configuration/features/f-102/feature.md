---
id: F-102
phase: 1
status: planned
---

# F-102 Typed DTO Serialization & Namespace Replacement/Merge

## Summary

Enables reading and writing strongly typed settings DTOs either under a single configuration key (`GetAsAsync<T>(ns, key)`, `SetAsAsync<T>(ns, key, value)`) or decomposed across top-level properties in a namespace (`GetAsAsync<T>(ns)`, `SetAsAsync<T>(ns, value)`, `MergeSettingsAsync<T>(ns, value)`), while preserving per-property storage providers and string encoding metadata.

## User Need

Application modules want to save and load cohesive settings objects in a single call while retaining the flexibility to encrypt specific properties locally (`aesLocal`) or store specific secrets in a cloud vault.

## Use Cases

- [UC-001: Save and Read Whole Settings Object Under One Key](uc-001-save-and-read-whole-settings-object-under-one-key.md)
- [UC-002: Save Namespace Settings and Replace Omitted Properties](uc-002-save-namespace-settings-and-replace-omitted-properties.md)
- [UC-003: Merge Partial Settings Without Removing Omitted Properties](uc-003-merge-partial-settings-without-removing-omitted-properties.md)
- [UC-004: Read String Values Without Losing Literal Quotes or Formatting](uc-004-read-string-values-without-losing-literal-quotes-or-formatting.md)
- [UC-005: Handle Nested Objects Nulls and Empty Namespaces](uc-005-handle-nested-objects-nulls-and-empty-namespaces.md)

## Scope

- **Keyed DTO operations (`GetAsAsync<T>(ns, key)`, `SetAsAsync<T>(ns, key, value, provider?)`):**
  - Stores the complete DTO under `key` using a single provider (plaintext JSON object when unencrypted, or serialized JSON with `"json": true` when stored via `"aesLocal"` or a vault provider).
  - Preserves existing provider on update when `provider` is omitted.
- **Namespace DTO operations (`GetAsAsync<T>(ns)`, `SetAsAsync<T>(ns, value)`, `MergeSettingsAsync<T>(ns, value)`):**
  - Decomposes a settings object so each top-level serialized property becomes an entry in `<namespace>.json` (keeping nested objects together within their top-level property's entry).
  - `SetAsAsync(ns, value)` replaces the namespace: preserves recorded providers for included properties, writes new properties as plaintext, and removes omitted local entries (leaving any remote vault secrets untouched unless explicitly removed).
  - `MergeSettingsAsync(ns, value)` performs a shallow partial update: updates only the supplied top-level properties (preserving their recorded providers) and leaves omitted namespace keys untouched.
  - `GetAsAsync<T>(ns)` resolves every entry in `<namespace>.json` through its recorded provider and assembles the complete DTO; throws `NotFound` when the namespace is empty or missing.
- **Typed string reads (`GetAsAsync<string>(ns, key)`):**
  - Uses the entry's `"json": true` metadata flag to distinguish raw unencoded strings written via `SetAsync` (e.g., `"123"`, `"null"`, or strings with quotes) from JSON-encoded strings written via keyed `SetAsAsync<string>`.
- **Legacy `namespaceSettings` compatibility:**
  - Reads legacy single-entry `namespaceSettings` documents transparently.
  - Replaces plaintext legacy `namespaceSettings` on namespace `SetAsAsync`; expands and preserves omitted properties on `MergeSettingsAsync`; fails with `Conflict` if a legacy `namespaceSettings` entry is provider-protected during property decomposition.
- **AOT / Schema-Safe Serialization:**
  - Supports `JsonTypeInfo<T>` overloads in .NET for Native AOT/trimming and optional validator/reviver hooks in TypeScript.

## Out of Scope

- Deep recursive merging of nested child objects during `MergeSettingsAsync` (merging is intentionally shallow at the top-level namespace property boundary).
- Two-phase recovery handles for interrupted namespace writes (owned by `F-204`).

## Inputs

- Target `namespace`, optional `key`, DTO instance `TValue`, optional `provider`, and optional serializer type info / schema metadata.

## Outputs

- Single-entry or multi-entry `<namespace>.json` files with accurate `"json": true` markers and preserved per-property provider bindings.
- Deserialized `TValue` instances assembled from plain, locally encrypted, OAuth, and cloud vault entries.

## Acceptance

- Saving a DTO at namespace scope creates one entry per top-level property, preserves existing providers on included properties, removes omitted entries on `SetAsAsync`, and retains omitted entries on `MergeSettingsAsync`.
- Reading a namespace DTO resolves mixed plaintext, `"aesLocal"`, and vault-backed properties into a single strongly typed object.
- `GetAsAsync<string>(ns, key)` returns exact unquoted strings whether saved via `SetAsync`, keyed `SetAsAsync<string>`, or namespace `SetAsAsync`.
- Legacy `namespaceSettings` files remain readable and migrate cleanly according to the conflict/expansion rules.
- Both `.NET` and `TypeScript/Node.js` implementations pass full DTO, namespace write, and legacy compatibility test suites.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-101](../../features/f-101/feature.md)
