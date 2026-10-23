---
feature: F-201
id: UC-001
status: planned
---

# UC-001 Move Setting to a New Provider

## Actor

Application Service or Operator Migration Command (`IConfiguration.MoveAsync`).

## Goal

Migrate an existing setting or secret from its current storage provider (such as plaintext or `"aesLocal"`) to a different provider (such as `"aesLocal"` or a cloud key vault) by writing to the new provider first before deleting the old copy.

## Starting Context

An entry already exists in `<namespace>.json` (for example, `"server"/"port"` stored in plaintext, or `"server"/"apiKey"` stored in `"aesLocal"`).

## Entry Route

Called via `var moved = await configuration.MoveAsync("server", "port", "aesLocal", cancellationToken)`.

## Trigger

An application or operator promotes a setting to local encryption or a cloud vault, or moves a secret between providers.

## Process

```text
Actor moves an existing setting to a new provider
+-- Starting context: key exists in <namespace>.json under source provider
+-- Entry route: IConfiguration.MoveAsync(namespace, key, toProvider)
+-- Trigger: caller requests provider change for an existing key
+-- Step 1: Acquire <namespace>.json lock and validate move request
|   +-- Key does not exist -> Fail with NotFound
|   +-- Destination provider equals current provider -> Fail with Conflict
|   \-- Destination provider is valid and differs from current -> Proceed
+-- Step 2: Resolve current value through source provider
|   +-- Read plaintext literal, decrypt "aesLocal", or fetch secret from source vault
|   \-- Preserve entry's "json": true flag if it holds a serialized DTO or JSON string
+-- Step 3: Write resolved value to destination provider FIRST
|   +-- Call destinationProvider.WriteAsync(resolvedValue, namespace, key)
|   +-- Update entry in <namespace>.json with new provider envelope
|   \-- Publish updated <namespace>.json atomically
+-- Step 4: Erase old copy from source provider
|   +-- Source was plaintext literal or local-only ("aesLocal"):
|   |   \-- Overwriting the local entry already removed the old copy -> SourceWithdrawn = true
|   \-- Source was a remote cloud vault:
|       +-- Call sourceProvider.EraseAsync(oldEntry, namespace, key)
|       \-- RemoteValueRemains == false -> SourceWithdrawn = true
\-- Success: Entry is active at destination provider; ConfigurationMove(SourceWithdrawn: true) returned
```

## Step Details

### Step 3: Write resolved value to destination provider FIRST
- Writing to the destination and publishing `<namespace>.json` *before* attempting to delete from the source guarantees that a failure during the move never loses the secret. Once Step 3 finishes, reads immediately use the new provider.

## Success

The setting is stored under the new provider in `<namespace>.json`, the old copy is withdrawn, and `moved.SourceWithdrawn` is `true`.

## Failure Cases

- `NotFound`: The specified `namespace` or `key` does not exist.
- `Conflict`: The entry is already stored under `toProvider`.
- `Validation`: `toProvider` is not a registered provider, or moving a vault's own credential into that vault would create a `CircularCredential`.

## Consistency Check

- Unique coverage: Covers moving an existing setting to a named provider (`"aesLocal"` or cloud vault) with clean source withdrawal.
- Closest related use cases: `UC-002` returns a protected setting back to default plaintext storage; `UC-003` handles retained or failed source cleanup after destination publication.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: destination is written before source erasure, `"json": true` metadata is preserved, and `SourceWithdrawn` is `true` when the old copy is gone.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
