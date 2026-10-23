---
feature: F-102
id: UC-003
status: planned
---

# UC-003 Merge Partial Settings Without Removing Omitted Properties

## Actor

Application Module Developer (`IConfiguration.MergeSettingsAsync<T>`).

## Goal

Update only the supplied settings in a namespace while leaving all other existing properties in that namespace completely untouched.

## Starting Context

A namespace file (`mail.json`) exists on disk with multiple properties (e.g. `Host: "smtp.old.test"`, `Port: 587`, `Password: "<encrypted>"`).

## Entry Route

Called via `await configuration.MergeSettingsAsync("mail", new { Host = "smtp.new.test" })`.

## Trigger

An application module wants to update one or two properties in a namespace without overwriting or erasing the rest of the settings.

## Process

```text
Actor partially updates namespace settings
+-- Starting context: namespace file has existing settings, caller supplies partial object
+-- Entry route: MergeSettingsAsync(namespace, partialSettings)
+-- Trigger: application updates specific properties
+-- Step 1: Serialize partial object to top-level properties
|   \-- Example: new { Host = "smtp.new.test" } yields property "Host"
+-- Step 2: Read existing <namespace>.json document
+-- Step 3: Apply partial updates
|   +-- Update "Host" value
|   +-- Preserve recorded provider for "Host" (or default to plaintext if new)
|   \-- Leave "Port", "Password", and all other existing keys in the file unchanged
+-- Step 4: Write updated document atomically
\-- Success: "Host" is updated; "Port" and "Password" remain intact
```

## Step Details

### Step 3: Apply partial updates
- **Shallow Merge:** Merging happens at the top-level property boundary. If a complex child object like `Retry` is supplied (`new { Retry = new { Attempts = 5 } }`), it replaces the entire `Retry` property.
- **Provider Preservation:** Any existing provider (e.g. `"aesLocal"` or a vault provider) associated with the updated property is preserved.

## Success

The namespace file is updated with the new property values, while omitted keys remain exactly as they were before the call.

## Failure Cases

- `Validation`: The supplied object is null, empty, or cannot be serialized into property mappings.
- `NotWritable`: Permissions or disk error updating the file.

## Consistency Check

- Unique coverage: Covers partial namespace updates preserving omitted keys.
- Closest related use cases: `UC-002` replaces the entire namespace and deletes omitted keys.
- Contradictions: None found.

## Acceptance

- Supplied properties are updated in `<namespace>.json`.
- Omitted properties remain untouched in the file.
- Individual providers on existing keys are preserved.
