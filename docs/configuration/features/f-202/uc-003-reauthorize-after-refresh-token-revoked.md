---
feature: F-202
id: UC-003
status: planned
---

# UC-003 Recover When a Refresh Token Is Revoked

## Actor

Application Service (`IConfiguration.GetAsync`) and Interactive CLI Setup Command (`IConfigurationExtensions.ReauthorizeAsync`).

## Goal

Mark an OAuth credential as requiring re-authorization when the server rejects its refresh token—failing ordinary reads fast without popping open a browser—and allow an interactive command to sign in again using the server details already stored in the entry.

## Starting Context

An `"oauth"` entry in `<namespace>.json` is within 5 minutes of expiry (or expired), and the authorization server has revoked the user's refresh grant.

## Entry Route

1. Ordinary read: `await configuration.GetAsync("mail", "token")`.
2. Interactive recovery: `await authorization.ReauthorizeAsync("mail", "token")`.

## Trigger

An ordinary `GetAsync` read attempts to refresh an expired token whose refresh grant was revoked by the server, followed by the user running an interactive re-login command.

## Process

```text
Actor hits revoked refresh grant and reauthorizes interactively
+-- Starting context: stored refresh token has been revoked at the authorization server
+-- Entry route: GetAsync(namespace, key) followed by ReauthorizeAsync(namespace, key)
+-- Trigger: token refresh fails with Forbidden (invalid_grant)
+-- Phase A: Non-Interactive Read Marks Entry as Dead
|   +-- GetAsync enters locked refresh and calls TokenEndpoint with stored refresh_token
|   +-- Server rejects refresh token (HTTP 400/401 invalid_grant -> Forbidden)
|   +-- Library updates <namespace>.json under lock:
|   |   \-- Sets "reauth_required": true on the entry (keeping encrypted server metadata)
|   +-- Library does NOT open a browser
|   \-- GetAsync throws ResultException [Forbidden] ("re-authorization required for 'mail/token'")
+-- Phase B: Subsequent Reads Fail Fast
|   +-- Any later GetAsync("mail", "token") sees "reauth_required": true
|   \-- Fails immediately as Forbidden without calling the token endpoint again
+-- Phase C: Interactive Reauthorization Restores Access
|   +-- User runs interactive command calling ReauthorizeAsync("mail", "token")
|   +-- Library decrypts existing entry to read stored OAuthOptions (endpoints, clientId, scopes, port)
|   +-- Library runs interactive PKCE loopback browser flow (same as UC-001)
|   +-- User grants consent; server issues new access_token and refresh_token
|   \-- Library seals new grant and saves entry with "reauth_required" cleared (false/omitted)
\-- Success: Subsequent GetAsync("mail", "token") calls return the new access token
```

## Step Details

### Phase A: Non-Interactive Read Marks Entry as Dead
- Background services and non-interactive CLI commands must never surprise a user by launching a web browser during `GetAsync`.
- Marking `"reauth_required": true` on disk ensures that subsequent reads fail immediately without repeatedly sending a dead refresh token to the authorization server.

### Phase C: Interactive Reauthorization Restores Access
- `ReauthorizeAsync(namespace, key)` takes only the `namespace` and `key`—no `OAuthOptions` parameter is needed or accepted, preventing the caller from accidentally supplying mismatched server settings.

## Success

The revoked credential fails cleanly during non-interactive reads, and a single `ReauthorizeAsync(namespace, key)` call restores a fresh encrypted grant.

## Failure Cases

- `ReauthorizationRequired`: Thrown on `GetAsync` when the refresh token is rejected or `"reauth_required": true` is set (`ErrorCategory.Forbidden`).
- `AuthorizationRefused` / `StateMismatch`: Returned by `ReauthorizeAsync` if the user declines or cancels the interactive browser flow (leaving the existing entry marked `reauth_required: true`).

## Consistency Check

- Unique coverage: Covers refresh token revocation, persistent `reauth_required` marking, fast-fail reads, and option-less `ReauthorizeAsync(namespace, key)`.
- Closest related use cases: `UC-001` covers first-time `AuthorizeAsync` with explicit `OAuthOptions`; `UC-002` covers successful token refresh.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: rejected refresh sets `"reauth_required": true`, opens 0 browsers during `GetAsync`, and `ReauthorizeAsync` clears `"reauth_required"` after browser sign-in.
- Failure cases produce clear guidance naming `namespace/key`.
- The consistency check shows no unresolved duplicate coverage or contradictions.
