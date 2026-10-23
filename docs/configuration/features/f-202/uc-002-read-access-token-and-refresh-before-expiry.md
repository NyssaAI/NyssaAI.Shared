---
feature: F-202
id: UC-002
status: planned
---

# UC-002 Read Access Token and Automatically Refresh Before Expiry

## Actor

Application Service (`IConfiguration.GetAsync`, `OAuthTokenProvider`).

## Goal

Read a current bearer access token through a normal `GetAsync(namespace, key)` call, returning it immediately when fresh or transparently refreshing it under a lock when it is within 5 minutes of expiring—without ever opening a browser or making duplicate refresh calls.

## Starting Context

An encrypted `"oauth"` entry already exists in `<namespace>.json` (from `UC-001`), and `reauth_required` is not set. One or more application readers request the token.

## Entry Route

Called via `await configuration.GetAsync("mail", "token", cancellationToken)`.

## Trigger

An application service needs an access token to call an external API.

## Process

```text
Actor reads access token from an OAuth configuration entry
+-- Starting context: sealed "oauth" entry exists in <namespace>.json
+-- Entry route: IConfiguration.GetAsync(namespace, key)
+-- Trigger: application requests bearer token for an API call
+-- Step 1: Check reauth_required flag on entry
|   +-- Entry has "reauth_required": true
|   |   \-- Fail immediately with ReauthorizationRequired [Forbidden] (no network call, no browser)
|   \-- Flag is false -> Proceed
+-- Step 2: Decrypt entry and check token freshness (Unlocked Fast Path)
|   +-- Decrypt OAuthToken using AES-256-GCM cipher
|   +-- Compare (ExpiresAt - CurrentUtcTime) against 5-minute refresh skew
|   +-- Token has more than 5 minutes left (Fresh)
|   |   \-- Return AccessToken immediately without acquiring write lock or calling server
|   \-- Token has 5 minutes or less remaining (Stale) -> Proceed to Step 3
+-- Step 3: Acquire namespace lock and re-read entry (Double-Checked Locking)
|   +-- Acquire <namespace>.json.lock via ModifyAsync
|   +-- Re-read and decrypt entry inside the lock
|   +-- Another concurrent reader already refreshed the token while we waited
|   |   \-- Return the newly stored AccessToken without calling the token endpoint again
|   \-- Entry is still stale -> Proceed to Step 4
+-- Step 4: Refresh grant at token endpoint
|   +-- Send refresh_token to the server's TokenEndpoint recorded in the entry
|   +-- Server returns new AccessToken (and optional rotated RefreshToken)
|   +-- Seal updated OAuthToken and publish <namespace>.json under the lock
|   \-- Return new AccessToken to caller
\-- Success: Valid bearer access token returned to application
```

## Step Details

### Step 2: Decrypt entry and check token freshness (Unlocked Fast Path)
- While `ExpiresAt - clock.GetUtcNow() > TimeSpan.FromMinutes(5)`, reads do not take a write lock or make any HTTP request; they simply decrypt the local entry and return `AccessToken`.

### Step 3: Acquire namespace lock and re-read entry (Double-Checked Locking)
- When 5 or more concurrent callers hit an expiring token at the same time, all of them enter `RefreshUnderLockAsync`, which serializes through the namespace's in-process gate and cross-process `.lock` file (`F-001`).
- The first caller refreshes the token and writes the updated entry to `<namespace>.json`. Each waiting caller re-reads `<namespace>.json` after acquiring the lock, sees that `IsFresh(stored)` is now true, and returns the new access token immediately—producing **1 HTTP refresh call across N concurrent readers**.

## Success

The caller receives a valid, non-expired access token, and any refreshed grant is persisted encrypted in `<namespace>.json`.

## Failure Cases

- `ReauthorizationRequired`: The refresh token was rejected by the server (`ErrorCategory.Forbidden`); see `UC-003`.
- `EntryChanged`: The entry was removed or switched to another provider while waiting for the lock (`ErrorCategory.Conflict`).
- `Unavailable`: Temporary network error contacting the token endpoint or lock timeout waiting for `<namespace>.json.lock`.

## Consistency Check

- Unique coverage: Covers non-interactive access token reads, the 5-minute refresh skew fast path, and double-checked locked refresh coordination across concurrent readers.
- Closest related use cases: `UC-001` acquires the initial grant; `UC-003` handles permanent refresh rejection (`reauth_required`); `UC-004` handles local disk write failure after a successful token rotation.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: fresh reads make 0 HTTP calls; stale reads within 5 minutes of expiry make 1 HTTP refresh call across concurrent readers and never launch a browser.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
