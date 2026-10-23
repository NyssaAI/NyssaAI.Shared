---
feature: F-202
id: UC-004
status: planned
---

# UC-004 Preserve Rotated Refresh Token in Memory If Disk Write Fails

## Actor

OAuth Token Provider (`OAuthTokenProvider`).

## Goal

Prevent permanent loss of a user's OAuth session when the authorization server rotates the refresh token during a refresh call, but writing the updated `<namespace>.json` file to disk fails transiently.

## Starting Context

An `"oauth"` entry in `<namespace>.json` is being refreshed (`UC-002`). The authorization server uses refresh-token rotation (invalidating the old refresh token as soon as it issues the new one).

## Entry Route

Triggered internally inside `OAuthTokenProvider.RefreshUnderLockAsync` and `ReadAsync` during `IConfiguration.GetAsync(namespace, key)`.

## Trigger

The token endpoint returns a newly rotated grant (`access_token` + new `refresh_token`), but publishing the updated `<namespace>.json` file fails due to a transient disk or file-sharing error.

## Process

```text
Actor preserves rotated OAuth grant when local file publish fails
+-- Starting context: token endpoint succeeded and rotated the refresh token
+-- Entry route: OAuthTokenProvider refresh persistence path
+-- Trigger: initial ModifyAsync file publication fails after token exchange succeeded
+-- Step 1: Retry persisting the rotated grant immediately (up to 3 attempts)
|   +-- Open <namespace>.json under lock (PersistHeldAsync)
|   +-- Verify entry still matches the original pre-refresh snapshot (or already has the new grant)
|   |   \-- Entry was replaced or removed by another operation -> Fail with EntryChanged [Conflict]
|   +-- Attempt to seal and publish rotated grant to <namespace>.json
|   \-- Retry succeeds -> Return new AccessToken to caller
+-- Step 2: Hold rotated grant in memory if all 3 disk attempts fail
|   +-- Store HeldGrant (rotated OAuthToken + original entry snapshot) in in-memory dictionary by "namespace/key"
|   +-- Log warning that grant was refreshed but could not be persisted
|   \-- Return persistence failure for the current read
+-- Step 3: Republish held grant on the next ReadAsync call
|   +-- Next GetAsync(namespace, key) checks in-memory held grants first
|   +-- Calls PersistHeldAsync to write the held grant to <namespace>.json without calling the server
|   +-- Disk write succeeds:
|   |   \-- Remove from held memory map and return AccessToken
|   \-- Disk write still fails (non-Conflict):
|       \-- Keep in memory for the next read and return failure
\-- Success: Rotated refresh token is eventually saved to disk without re-contacting the server
```

## Step Details

### Step 2: Hold rotated grant in memory if all 3 disk attempts fail
- Once an OAuth server rotates a refresh token, the old refresh token on disk is dead. If the library discarded the new refresh token when disk publication failed, the next read would send the old refresh token from disk, causing the server to reject the grant and force the user to sign in again.
- Holding the rotated `HeldGrant` in memory ensures the next read writes the already-exchanged grant to disk first.
- If the entry is explicitly removed (`EraseAsync`) or re-authorized (`AuthorizeAsync`), any held grant for that `namespace/key` is cleared from memory so a deleted credential is never resurrected.

## Success

The rotated grant survives transient local disk errors and is persisted on the retry or subsequent read without losing the session or calling the authorization server twice.

## Failure Cases

- `EntryChanged`: Another caller modified, moved, or deleted the entry while the grant was held (`ErrorCategory.Conflict`), causing the held grant to be discarded safely.
- `NotWritable`: Disk remains unwritable across retries; the grant stays held in memory for the lifetime of the provider instance.

## Consistency Check

- Unique coverage: Covers in-memory retention and safe republication of rotated OAuth grants after local disk write failures.
- Closest related use cases: `UC-002` covers normal locked token refresh when disk publication succeeds.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: transient publish failure retries up to 3 times, holds the rotated token in memory if still failing, and persists it on the next read without a second HTTP refresh call.
- Failure cases (such as concurrent entry deletion/replacement) discard the held grant rather than overwriting newer data.
- The consistency check shows no unresolved duplicate coverage or contradictions.
