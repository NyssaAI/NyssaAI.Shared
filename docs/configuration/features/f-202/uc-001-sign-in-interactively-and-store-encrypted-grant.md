---
feature: F-202
id: UC-001
status: planned
---

# UC-001 Sign In Interactively and Store Encrypted Grant

## Actor

Interactive CLI Command or Setup Workflow (`IConfigurationExtensions.AuthorizeAsync`).

## Goal

Sign a user in to an external service through their web browser using PKCE, exchange the authorization code for an access token and refresh token, and save the complete credential encrypted on disk under the `"oauth"` provider.

## Starting Context

The application has an interactive command (such as `my-cli auth mail`) and knows the service's `OAuthOptions` (HTTPS authorization endpoint, HTTPS token endpoint, client ID, requested scopes, and optional loopback redirect port).

## Entry Route

Called via `await authorization.AuthorizeAsync("mail", "token", oauthOptions, cancellationToken)`.

## Trigger

A user runs an interactive setup or login command that explicitly initiates browser sign-in.

## Process

```text
Actor runs interactive browser sign-in for a service
+-- Starting context: OAuthOptions supplied with server endpoints, clientId, and scopes
+-- Entry route: IConfigurationExtensions.AuthorizeAsync(namespace, key, options)
+-- Trigger: interactive CLI login or setup command invoked
+-- Step 1: Validate OAuthOptions
|   +-- Both AuthorizationEndpoint and TokenEndpoint must use https://
|   +-- ClientId must not be blank
|   \-- Validation fails -> Return Result.Failure [Validation] before opening browser
+-- Step 2: Start local loopback listener and generate PKCE challenge
|   +-- Bind HTTP listener on http://127.0.0.1:<port>/ (ephemeral or RedirectPort)
|   \-- Generate PKCE verifier, S256 code_challenge, and random state token
+-- Step 3: Launch user's browser to authorization URL
|   +-- Call IBrowserLauncher.LaunchAsync(authorizeUri)
|   \-- Browser launch fails -> Dispose listener, return Result.Failure
+-- Step 4: Wait for browser redirect callback on loopback listener
|   +-- Callback contains "error" (e.g. user clicked Deny / access_denied)
|   |   \-- Return Result.Failure(AuthorizationRefused) [Forbidden]; store nothing
|   +-- Callback "state" does not match generated state
|   |   \-- Return Result.Failure(StateMismatch) [Forbidden]; discard response
|   \-- Callback contains matching state and authorization "code" -> Proceed
+-- Step 5: Exchange authorization code at token endpoint
|   +-- Send code, PKCE code_verifier, client_id, and redirect_uri over HTTPS
|   +-- Server returns no refresh_token
|   |   \-- Return Result.Failure(NoRefreshToken) [Validation]; refuse grant
|   \-- Server returns access_token, refresh_token, and expires_in -> Proceed
+-- Step 6: Seal and persist self-describing OAuthToken
|   +-- Bundle tokens together with server endpoints, clientId, scopes, and redirectPort
|   +-- Encrypt bundle using AES-256-GCM bound to (namespace, key)
|   \-- Save entry in <namespace>.json with "provider": "oauth" and "encrypted": true
\-- Success: Encrypted self-describing OAuth grant stored on disk; Result.Success returned
```

## Step Details

### Step 2: Start local loopback listener and generate PKCE challenge
- Follows RFC 8252 for native applications: binds a temporary loopback listener on `127.0.0.1`, generates a cryptographic PKCE `S256` challenge and random `state` parameter, and always disposes the listener port when the flow finishes, fails, or is canceled.

### Step 6: Seal and persist self-describing OAuthToken
- The stored credential includes not only the access token, refresh token, and expiry timestamp, but also the `AuthorizationEndpoint`, `TokenEndpoint`, `ClientId`, `Scopes`, and `RedirectPort`.
- Because the grant is self-describing and sealed with `IConfigurationCipher` (`F-003`), neither tokens nor server URLs appear in plaintext in `<namespace>.json`, and future token refreshes or reauthorizations do not need the application to pass `OAuthOptions` again.

## Success

The user completes sign-in in the browser, and `<namespace>.json` holds an encrypted `"oauth"` entry ready for ordinary `GetAsync` reads.

## Failure Cases

- `Validation`: Endpoints are not HTTPS, `ClientId` is missing, the callback lacks an authorization code, or the server did not issue a refresh token (`NoRefreshToken`).
- `AuthorizationRefused`: The user declined consent or the authorization server returned an OAuth error (`ErrorCategory.Forbidden`).
- `StateMismatch`: The callback `state` parameter did not match the request (`ErrorCategory.Forbidden`).
- `Canceled`: The user or caller canceled the sign-in flow while waiting for the browser callback.

## Consistency Check

- Unique coverage: Covers initial interactive PKCE sign-in and sealing of a new `"oauth"` credential for an application service.
- Closest related use cases: `UC-002` reads and refreshes this stored credential; `UC-003` reauthorizes it after revocation; `UC-005` authorizes a credential specifically for a cloud vault provider.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: `<namespace>.json` contains `"provider": "oauth"` and `"encrypted": true` with no plaintext tokens or URLs.
- Failure cases (such as user denying consent or missing refresh token) leave no partial entry on disk and return clear `Result` errors.
- The consistency check shows no unresolved duplicate coverage or contradictions.
