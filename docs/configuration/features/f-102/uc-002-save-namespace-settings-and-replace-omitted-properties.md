---
feature: F-102
id: UC-002
status: planned
---

# UC-002 Save Namespace Settings and Replace Omitted Properties

## Actor

Application Module Developer (`IConfiguration.SetAsAsync<T>`, `GetAsAsync<T>`).

## Goal

Save a complete settings object at namespace scope so that each top-level property becomes its own entry in the `.json` file, and remove any old keys that were left out of the new settings object.

## Starting Context

The application has an `IConfiguration` instance. A namespace file (e.g. `mail.json`) may already contain properties.

## Entry Route

Called via `await configuration.SetAsAsync("mail", mailSettings)` and `await configuration.GetAsAsync<MailSettings>("mail")`.

## Trigger

An application module saves a complete snapshot of its settings for a namespace.

## Process

```text
Actor saves complete namespace settings snapshot
+-- Starting context: populated DTO in memory, namespace specified (no key)
+-- Entry route: SetAsAsync(namespace, settings)
+-- Trigger: application replaces namespace settings
+-- Step 1: Serialize DTO into top-level properties
|   \-- Example: MailSettings yields "Host", "Port", "Retry"
+-- Step 2: Read existing entries in <namespace>.json
+-- Step 3: Replace namespace entries
|   +-- Included properties:
|   |   +-- If property already exists in file -> preserve its recorded provider (e.g. "aesLocal")
|   |   \-- If property is new -> save as plaintext
|   \-- Omitted properties:
|       \-- Any key in <namespace>.json NOT present in the DTO is removed from the local file
+-- Step 4: Write updated document atomically
\-- Success: <namespace>.json contains exactly the properties from the supplied object
```

## Step Details

### Step 3: Replace namespace entries
- **Provider Preservation:** If a property like `Password` was previously moved to `"aesLocal"`, updating the namespace via `SetAsAsync` preserves `"aesLocal"` on `Password` while updating its value.
- **Replacement Semantics:** Any setting currently in the `.json` file that is absent from the new object is deleted locally. (If omitted entries should remain unchanged, use `MergeSettingsAsync` described in `UC-003`).

## Success

The namespace file on disk mirrors the serialized properties of the settings object, preserving existing provider assignments while removing stale settings.

## Failure Cases

- `NotFound`: Reading an empty or absent namespace throws or fails.
- `Validation`: Input cannot be serialized into property entries.

## Consistency Check

- Unique coverage: Covers namespace-wide replacement where omitted properties are removed.
- Closest related use cases: `UC-001` stores under a single key; `UC-003` preserves omitted properties.
- Contradictions: None found.

## Acceptance

- Each top-level property gets its own entry in `<namespace>.json`.
- Previously encrypted properties remain encrypted with `"aesLocal"`.
- Properties omitted from the DTO are deleted from the local file.
