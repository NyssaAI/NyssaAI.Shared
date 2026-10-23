---
feature: F-102
id: UC-005
status: planned
---

# UC-005 Handle Nested Objects Nulls and Empty Namespaces

## Actor

Application Module Developer / Configuration Store (`ConfigurationStore.Namespace.cs`).

## Goal

Correctly handle complex JSON data structures—including nested child objects, explicit `null` values, omitted optional properties, and empty object payloads—without losing data or unintentionally corrupting sibling properties.

## Starting Context

The application is saving or updating a namespace using `SetAsAsync` or `MergeSettingsAsync`, and the settings object contains nested objects, null values, or empty sets.

## Entry Route

Invoked when calling `SetAsAsync<T>(namespace, settings)` or `MergeSettingsAsync<T>(namespace, settings)`.

## Trigger

An application module executes a full or partial namespace update containing nested objects, nulls, or an empty object.

## Process

```text
Actor handles nested objects, nulls, and edge cases
+-- Starting context: DTO with complex structures, nulls, or empty payload
+-- Entry route: SetAsAsync(namespace, settings) or MergeSettingsAsync(namespace, settings)
+-- Trigger: application saves configuration snapshot
+-- Case 1: Nested Child Objects (e.g. MailSettings with nested Retry = { Attempts, Delay })
|   +-- Top-level property "Retry" becomes the entry key in <namespace>.json
|   +-- Nested child object is preserved completely inside the "value" envelope:
|   |   \-- "Retry": { "value": { "Attempts": 3, "DelaySeconds": 5 } }
|   \-- In MergeSettingsAsync: Merging is SHALLOW at the property boundary
|       \-- Supplying new { Retry = new { Attempts = 10 } } replaces the whole Retry object
|           (DelaySeconds is NOT merged recursively; it is replaced)
+-- Case 2: Explicit Null vs. Omitted Property
|   +-- Explicit null (property emitted by serializer as null):
|   |   \-- Stored as "ApiKey": { "value": null } preserving the key
|   \-- Omitted property (undefined or omitted by serializer):
|       +-- If SetAsAsync: Omitted key is removed from <namespace>.json
|       \-- If MergeSettingsAsync: Omitted key is left untouched in <namespace>.json
+-- Case 3: Empty Object ({})
|   +-- SetAsAsync(namespace, new {}):
|   |   +-- Clears all entries in <namespace>.json, leaving {}
|   |   \-- Subsequent GetAsAsync<T>(namespace) fails with NotFound
|   \-- MergeSettingsAsync(namespace, new {}):
|       \-- Leaves existing entries completely unchanged
+-- Case 4: Legacy "namespaceSettings" Entry
|   +-- Read: automatically unwraps legacy single-entry objects
|   +-- SetAsAsync: decomposes into modern individual property keys and removes "namespaceSettings"
|   \-- Conflict: if legacy object was encrypted with "aesLocal", property merge throws Conflict
\-- Success: JSON state accurately reflects exact caller intent
```

## Step Details

### Case 1: Shallow Merge vs. Deep Merge
- Merging across namespace operations is explicitly **shallow**. If an object has child properties, supplying an update for that object replaces the entire child structure. This prevents unintended zombie fields from surviving when a developer resets a sub-structure.

### Case 4: Legacy Format Migration
- Older configuration files stored entire DTOs under a magic key named `"namespaceSettings"`.
- Modern reads recognize and deserialize this automatically without user intervention.
- Modern namespace writes expand this legacy entry into top-level properties and clean up the legacy key. If the legacy key was protected under a provider like `"aesLocal"`, decomposing it without clear per-property intent throws a `Conflict` error, requiring an explicit move or rewrite.

## Success

Nested objects remain intact, explicit nulls are stored without breaking schemas, empty namespace replacements clear old data, and legacy files are safely migrated.

## Failure Cases

- `NotFound`: Reading an empty namespace (`{}`) throws because no configured settings exist.
- `Conflict`: Attempting to merge into an encrypted legacy `namespaceSettings` entry without decrypting and migrating properties first.

## Consistency Check

- Unique coverage: Specifically governs complex JSON data edge cases, nested structures, null handling, and legacy format migrations.
- Closest related use cases: `UC-002` (namespace replacement) and `UC-003` (partial merge).
- Contradictions: None found.

## Acceptance

- Nested objects are stored as cohesive JSON sub-objects under their top-level property key.
- `MergeSettingsAsync` on a nested object replaces the nested object entirely rather than recursively merging child fields.
- Calling `SetAsAsync` with an empty object removes all entries in the file.
- Calling `MergeSettingsAsync` with an empty object leaves existing entries untouched.
- Legacy `namespaceSettings` entries migrate to modern property keys cleanly.
