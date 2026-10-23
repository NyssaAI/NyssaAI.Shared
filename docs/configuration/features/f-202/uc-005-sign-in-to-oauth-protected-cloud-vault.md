---
feature: F-202
id: UC-005
status: planned
---

# UC-005 Sign In to an OAuth-Protected Cloud Vault

## Actor

Interactive CLI Setup Command (`IConfigurationExtensions.AuthorizeProviderAsync`) and Cloud Vault Adapter (`IAccessTokenSource`).

## Goal

Sign in interactively to a configured cloud key vault that authenticates with OAuth2, storing the vault's OAuth credential locally under `"oauth"` so the vault adapter automatically receives fresh bearer tokens during secret reads and writes.

## Starting Context

The application manifest (or `BootstrapOptions.KeyVault`) configures a cloud key vault (e.g. `name: "contoso"`, `kind: "fake"`, `credential: "vault/contoso-token"`), and the registered `ICloudKeyVaultFactory` returns `OAuthOptions` from `Authorization(vaultOptions)`.

## Entry Route

Called via `await authorization.AuthorizeProviderAsync("contoso", cancellationToken)`.

## Trigger

An operator or user sets up a cloud key vault that requires interactive OAuth sign-in before secrets can be read from or written to the vault.

## Process

```text
Actor authorizes an OAuth-backed cloud key vault
+-- Starting context: vault configured with provider name "contoso" and credential "vault/contoso-token"
+-- Entry route: IConfigurationExtensions.AuthorizeProviderAsync("contoso")
+-- Trigger: user runs interactive vault login command
+-- Step 1: Guard against unauthenticated vault access prior to sign-in
|   +-- If app calls SetAsync/GetAsync with provider "contoso" before sign-in:
|   \-- Fails with ResultException ("authorization required for 'contoso'") without opening browser
+-- Step 2: Resolve vault OAuth settings and credential target
|   +-- Look up configured vault "contoso" and its ICloudKeyVaultFactory
|   +-- Obtain OAuthOptions from factory.Authorization(vaultOptions)
|   \-- Parse vaultOptions.Credential ("vault/contoso-token") into namespace "vault", key "contoso-token"
+-- Step 3: Run interactive PKCE sign-in (UC-001)
|   +-- Launch browser, receive loopback callback, exchange code for tokens
|   \-- Seal self-describing OAuthToken into vault.json under key "contoso-token" with provider "oauth"
+-- Step 4: Supply bearer tokens to vault adapter on demand
|   +-- App calls SetAsync("mail", "password", "secret", provider: "contoso") or GetAsync
|   +-- Vault adapter requests bearer token from IAccessTokenSource
|   +-- AccessTokenSource reads "vault"/"contoso-token" through ConfigurationStore (UC-002)
|   +-- Refreshes token automatically when within 5-minute expiry skew
|   \-- Vault adapter receives only the current access token (never sees refresh token or cipher key)
\-- Success: Cloud vault reads and writes authenticate seamlessly with auto-refreshing bearer tokens
```

## Step Details

### Step 4: Supply bearer tokens to vault adapter on demand
- `AccessTokenSource` bridges `F-202` (OAuth) and `F-203` (Cloud Key Vaults): it reads the vault's credential entry (`"vault/contoso-token"`) through the local store.
- Because the credential is an ordinary `"oauth"` entry in `vault.json`, token refresh, locking, and reauthorization work identically to service tokens, and the external vault package never handles refresh tokens or encryption keys.
- Attempting to store `"vault/contoso-token"` inside the `"contoso"` vault itself is blocked as a `CircularCredential` error before contacting the vault (`F-203`).

## Success

The vault's OAuth grant is stored locally under `"oauth"`, and subsequent vault operations automatically obtain and refresh bearer tokens through `IAccessTokenSource`.

## Failure Cases

- `NotFound` / `Validation`: The named provider is not configured, does not declare a `credential` address, or its factory does not support OAuth authorization.
- `Forbidden`: Vault is accessed before `AuthorizeProviderAsync` has been run, or the user declines browser consent during `AuthorizeProviderAsync`.
- `CircularCredential`: Someone attempts to store the vault's own credential entry inside that same vault.

## Consistency Check

- Unique coverage: Covers `AuthorizeProviderAsync(provider)` and how an OAuth credential powers a cloud key vault via `IAccessTokenSource`.
- Closest related use cases: `UC-001` covers `AuthorizeAsync(namespace, key, options)` for direct service credentials; `UC-002` covers token refresh during reads.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: `AuthorizeProviderAsync("contoso")` seals the grant at the vault's configured `credential` entry, and vault reads/writes receive refreshed bearer tokens without launching a browser.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
