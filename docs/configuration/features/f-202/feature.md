---
id: F-202
phase: 2
status: planned
---

# F-202 Interactive OAuth Sign-In & Automatic Token Refresh

## Summary

Implements interactive PKCE OAuth2 authorization (`IConfigurationExtensions`) and the `"oauth"` storage provider (`OAuthTokenProvider`) that seals self-describing grants locally, refreshes expiring access tokens under namespace locks during ordinary reads, and flags revoked grants with `reauth_required`.

## User Need

CLI tools and services that call OAuth-protected APIs or OAuth-authenticated cloud vaults need interactive browser sign-in during setup commands (`AuthorizeAsync`, `AuthorizeProviderAsync`, `ReauthorizeAsync`) and completely non-interactive, concurrency-safe access token reads (`GetAsync`) during normal operation.

## Use Cases

- [UC-001: Sign In Interactively and Store Encrypted Grant](uc-001-sign-in-interactively-and-store-encrypted-grant.md)
- [UC-002: Read Access Token and Automatically Refresh Before Expiry](uc-002-read-access-token-and-refresh-before-expiry.md)
- [UC-003: Recover When a Refresh Token Is Revoked](uc-003-reauthorize-after-refresh-token-revoked.md)
- [UC-004: Preserve Rotated Refresh Token in Memory If Disk Write Fails](uc-004-preserve-rotated-grant-on-disk-write-failure.md)
- [UC-005: Sign In to an OAuth-Protected Cloud Vault](uc-005-sign-in-to-oauth-protected-cloud-vault.md)
- [UC-006: Remove an OAuth Credential](uc-006-remove-oauth-credential.md)

## Scope

- `IConfigurationExtensions` interactive methods returning `Result`:
  - `AuthorizeAsync(namespace, key, oauthOptions)`: validates HTTPS endpoints, starts a local HTTP loopback listener (`127.0.0.1` on an ephemeral or configured `RedirectPort`), generates a PKCE S256 challenge and random `state`, opens the authorization URI via `IBrowserLauncher`, validates the callback `state`, exchanges the authorization code for tokens, and stores the sealed self-describing `OAuthToken` under provider `"oauth"`.
  - `AuthorizeProviderAsync(providerName)`: obtains `OAuthOptions` and credential target `(namespace, key)` from the configured cloud vault factory and runs the interactive authorization flow for that vault's credential entry.
  - `ReauthorizeAsync(namespace, key)`: reads the self-describing server metadata (`AuthorizationEndpoint`, `TokenEndpoint`, `ClientId`, `Scopes`, `RedirectPort`) already sealed inside an existing `"oauth"` entry and runs a fresh interactive PKCE flow to replace a revoked or expired grant.
- `OAuthTokenProvider` read & refresh lifecycle:
  - Fast path: when `GetAsync(namespace, key)` reads an `"oauth"` entry whose access token expires more than 5 minutes in the future (and `reauth_required` is false), returns the access token immediately without re-acquiring a write lock.
  - Synchronized refresh path: when within 5 minutes of expiry, acquires the namespace lock, re-reads the entry (double-checked locking so concurrent readers reuse a just-refreshed token), and calls the token endpoint with the refresh token.
  - Rotated grant durability: if the authorization server rotates the refresh token and succeeds, but writing the updated `<namespace>.json` fails transiently, holds the rotated grant in memory and republishes it on the next read so the grant is not lost.
  - Revoked grant handling: if the server rejects the refresh token (`invalid_grant` / 400 / 401), marks the local entry with `"reauth_required": true` and fails the read with `ErrorCategory.Forbidden` without ever launching a browser during `GetAsync`.

## Out of Scope

- Implicit flow, resource-owner password credentials flow, or non-HTTPS OAuth endpoints.
- Launching a browser during `IConfiguration.GetAsync` or `GetAsAsync`.

## Inputs

- `OAuthOptions` (`AuthorizationEndpoint`, `TokenEndpoint`, `ClientId`, `Scopes`, optional `RedirectPort`).
- Injected or default `IBrowserLauncher`, clock (`TimeProvider`), HTTP client, and `IConfigurationCipher`.

## Outputs

- Sealed `"oauth"` `ConfigurationValue` entries in `<namespace>.json` (with `encrypted: true`, `provider: "oauth"`, and optional `reauth_required: true`).
- Current bearer access tokens returned from `IConfiguration.GetAsync(namespace, key)`.

## Acceptance

- Both `AuthorizationEndpoint` and `TokenEndpoint` must use `https://`; non-HTTPS endpoints are rejected before opening a browser.
- Concurrent `GetAsync` calls on an expiring OAuth entry execute only one refresh request under the namespace lock.
- A rejected refresh grant marks the entry `reauth_required: true` and throws/fails with `Forbidden` without launching a browser; subsequent `ReauthorizeAsync(namespace, key)` restores access using the stored endpoint metadata.
- If a refresh succeeds with a rotated refresh token but local file publication fails, the next read republishes the in-memory rotated grant before attempting another refresh.
- Both `.NET` and `TypeScript/Node.js` implementations pass all OAuth tutorial and unit tests against a fake authorization server.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-003](../../features/f-003/feature.md)
- [F-101](../../features/f-101/feature.md)
- [F-103](../../features/f-103/feature.md)
