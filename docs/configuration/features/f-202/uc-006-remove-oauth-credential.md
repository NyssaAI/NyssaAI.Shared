---
feature: F-202
id: UC-006
status: planned
---

# UC-006 Remove an OAuth Credential

## Actor

Application Command or User Sign-Out Workflow (`IConfiguration.RemoveAsync`, `OAuthTokenProvider.EraseAsync`).

## Goal

Remove a stored OAuth credential from the local configuration store and clear any rotated grant held in memory so the signed-out credential cannot be used or accidentally republished by a later read.

## Starting Context

An `"oauth"` entry exists in `<namespace>.json` (and may also have an unpersisted rotated grant held in memory from `UC-004`).

## Entry Route

Called via `await configuration.RemoveAsync("mail", "token", cancellationToken)`.

## Trigger

A user runs a sign-out command or the application explicitly deletes the OAuth configuration entry.

## Process

```text
Actor removes a stored OAuth credential
+-- Starting context: "oauth" entry exists in <namespace>.json
+-- Entry route: IConfiguration.RemoveAsync(namespace, key)
+-- Trigger: sign-out command or credential removal requested
+-- Step 1: Acquire namespace lock and locate entry
|   +-- Entry is already absent from <namespace>.json
|   |   \-- Return ConfigurationRemoval { RemoteValueRemains = false } immediately
|   \-- Entry exists with provider "oauth" -> Proceed to Step 2
+-- Step 2: Execute provider erasure (OAuthTokenProvider.EraseAsync)
|   +-- Drop any rotated grant held in memory for "namespace/key"
|   +-- Do NOT contact the remote authorization server (removal is local withdrawal)
|   \-- Return ConfigurationRemoval { RemoteValueRemains = false }
+-- Step 3: Delete entry from <namespace>.json and publish atomically
\-- Success: Local OAuth grant and any in-memory held grant are removed
```

## Step Details

### Step 2: Execute provider erasure (`OAuthTokenProvider.EraseAsync`)
- Because the encrypted `OAuthToken` is stored inside `<namespace>.json` itself, removing the entry from the file deletes the stored access and refresh tokens from disk.
- `EraseAsync` also evicts any `HeldGrant` cached in memory (`_held.TryRemove("namespace/key")`) so a subsequent read cannot resurrect a credential that the user just removed.
- Local removal withdraws the credential from the machine (`RemoteValueRemains = false`); it does not call an OAuth token revocation endpoint on the remote authorization server.

## Success

The `"oauth"` entry is deleted from `<namespace>.json`, any in-memory held grant is cleared, `RemoveAsync` returns `RemoteValueRemains = false`, and subsequent `GetAsync` calls fail with `NotFound`.

## Failure Cases

- `NotWritable` / `Unavailable`: The namespace lock could not be acquired or `<namespace>.json` could not be written to disk, leaving the entry intact for retry.

## Consistency Check

- Unique coverage: Covers removing/signing out an OAuth credential and evicting any in-memory held grant.
- Closest related use cases: `UC-001` creates the credential; `UC-002` reads and refreshes it; `UC-004` holds rotated grants in memory on publish failure.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: the entry is removed from `<namespace>.json`, in-memory held grants for that address are dropped, `RemoteValueRemains` is `false`, and later reads throw `NotFound`.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
