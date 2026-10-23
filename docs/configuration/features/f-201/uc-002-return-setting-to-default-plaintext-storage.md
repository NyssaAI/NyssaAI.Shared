---
feature: F-201
id: UC-002
status: planned
---

# UC-002 Return Protected Setting to Default Plaintext Storage

## Actor

Application Service or Operator Command (`IConfiguration.MoveAsync`).

## Goal

Move a setting that is currently encrypted locally (`"aesLocal"`) or stored in a cloud key vault back to default plaintext JSON storage in `<namespace>.json`.

## Starting Context

An entry exists in `<namespace>.json` with a recorded provider (such as `"aesLocal"` or `"mailVault"`).

## Entry Route

Called by omitting `toProvider` (or passing `null`): `var returned = await configuration.MoveAsync("server", "port", cancellationToken: cancellationToken)`.

## Trigger

A caller wants to remove encryption or vault storage from an existing key while keeping its value in `<namespace>.json`.

## Process

```text
Actor returns a protected setting to default plaintext storage
+-- Starting context: key exists in <namespace>.json with provider "aesLocal" or vault
+-- Entry route: IConfiguration.MoveAsync(namespace, key, toProvider: null)
+-- Trigger: caller omits destination provider to return to default storage
+-- Step 1: Acquire <namespace>.json lock and check current provider
|   +-- Entry is already in default plaintext storage (provider is null)
|   |   \-- Fail with Conflict (already at destination)
|   \-- Entry uses a provider -> Proceed
+-- Step 2: Resolve plaintext value through current provider
|   \-- Decrypt "aesLocal" ciphertext or read secret from cloud vault
+-- Step 3: Publish plaintext entry to <namespace>.json
|   +-- Write compact entry {"value": <resolved>} (omitting provider and encrypted fields)
|   \-- Preserve "json": true if the entry holds a typed JSON value
+-- Step 4: Erase old copy from previous provider
|   +-- Previous provider was "aesLocal" -> Local overwrite already removed ciphertext (SourceWithdrawn = true)
|   \-- Previous provider was a cloud vault -> Call vault EraseAsync and report SourceWithdrawn
\-- Success: Entry is stored as plain JSON in <namespace>.json
```

## Step Details

### Step 3: Publish plaintext entry to `<namespace>.json`
- Returning to default storage writes the compact `ConfigurationValue` representation without `"provider"` or `"encrypted"`, matching how a brand-new plaintext entry is stored.

## Success

The entry is saved as plain JSON in `<namespace>.json`, the previous provider's copy is erased, and `ConfigurationMove` is returned.

## Failure Cases

- `NotFound`: The key does not exist.
- `Conflict`: The key is already stored in default plaintext storage.
- `Unavailable` / `Validation`: The current provider could not decrypt or read the value (leaving the original protected entry untouched).

## Consistency Check

- Unique coverage: Covers omitting `toProvider` in `MoveAsync` to return a protected entry to default plaintext storage.
- Closest related use cases: `UC-001` moves an entry to a named provider; `UC-003` covers source cleanup retention or failure.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: omitting `toProvider` writes plain JSON in `<namespace>.json` and removes `"provider"` and `"encrypted"` metadata.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
