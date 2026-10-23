---
feature: F-101
id: UC-003
status: planned
---

# UC-003 Remove a Setting and Report Remote Retention

## Actor

Application Service (`IConfiguration.RemoveAsync`).

## Goal

Delete a configuration setting from its recorded storage provider and remove its local entry from `<namespace>.json`, reporting whether a remote copy still remains in an external vault (such as a soft-deleted secret).

## Starting Context

A setting may exist in `<namespace>.json` as plaintext, `"aesLocal"`, `"oauth"`, or a cloud vault pointer—or may already be absent.

## Entry Route

Called via `var removal = await configuration.RemoveAsync(namespace, key, cancellationToken)`.

## Trigger

An application removes a setting or secret from configuration.

## Process

```text
Actor removes a setting and checks remote retention status
+-- Starting context: namespace and key supplied
+-- Entry route: IConfiguration.RemoveAsync(namespace, key)
+-- Trigger: application requests deletion of a key
+-- Step 1: Validate namespace and key names
+-- Step 2: Acquire <namespace>.json lock and look up entry
|   +-- Namespace file or key does not exist locally
|   |   \-- Return ConfigurationRemoval { RemoteValueRemains = false } without contacting any provider
|   \-- Entry exists -> Inspect entry.Provider
+-- Step 3: Request deletion from recorded provider (when provider is set)
|   +-- Plaintext literal (no provider):
|   |   \-- RemoteValueRemains = false
|   +-- Provider-backed entry ("aesLocal", "oauth", or cloud vault):
|       +-- Call provider.EraseAsync(entry, namespace, key)
|       +-- Provider erasure fails operationally (e.g. vault offline or permission denied):
|       |   \-- Abort removal, keep local entry intact in <namespace>.json for retry, and throw/fail
|       \-- Provider erasure succeeds:
|           \-- Capture provider's RemoteValueRemains flag (true if soft-deleted/retained, false if gone)
+-- Step 4: Remove entry from <namespace>.json and publish atomically
\-- Success: Local entry removed; ConfigurationRemoval(RemoteValueRemains) returned to caller
```

## Step Details

### Step 3: Request deletion from recorded provider
- If a remote vault is unreachable or denies deletion, `RemoveAsync` fails and keeps the local pointer in `<namespace>.json` so the caller can retry later without losing track of the remote secret.
- If the vault deletes the secret (or places it into a soft-delete retention state like Azure Key Vault), `EraseAsync` succeeds with `RemoteValueRemains = true`, the local entry is removed from `<namespace>.json`, and the caller learns via `removal.RemoteValueRemains` that a recoverable copy still exists in the vault.

## Success

The local entry is removed from `<namespace>.json`, and `ConfigurationRemoval.RemoteValueRemains` accurately reports whether a remote copy remains in the provider.

## Failure Cases

- `Validation`: Invalid `namespace` or `key` name, or the entry references an unregistered provider.
- `Unavailable` / `Forbidden`: The remote vault failed to process deletion, or `<namespace>.json` could not be locked/written.

## Consistency Check

- Unique coverage: Covers `RemoveAsync(namespace, key)` across providers and `ConfigurationRemoval.RemoteValueRemains` reporting.
- Closest related use cases: `UC-001` creates and reads keys; `F-202 UC-006` covers OAuth-specific in-memory held grant eviction during `RemoveAsync`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: absent keys succeed with `RemoteValueRemains = false`; soft-deleted vault secrets return `RemoteValueRemains = true`; operational vault failures preserve the local pointer for retry.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
