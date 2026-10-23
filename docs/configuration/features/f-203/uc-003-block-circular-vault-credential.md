---
feature: F-203
id: UC-003
status: planned
---

# UC-003 Block Circular Vault Credential Storage

## Actor

Configuration Store (`ConfigurationStore`) and Vault Access Token Source (`AccessTokenSource`).

## Goal

Prevent a deadlock where a cloud key vault's own authentication credential (such as `"vault/contoso-token"`) is stored inside that same cloud vault, refusing the operation before any network call is attempted.

## Starting Context

A cloud key vault (`name: "contoso"`) is configured to authenticate using a local credential entry (`credential: "vault/contoso-token"`).

## Entry Route

Triggered if application code calls `SetAsync("vault", "contoso-token", value, provider: "contoso")`, `MoveAsync("vault", "contoso-token", "contoso")`, or if `AccessTokenSource` inspects `"vault/contoso-token"` during a vault call.

## Trigger

An attempt to store the vault's credential entry inside the vault it authenticates, or to read a vault credential that points back to the same vault.

## Process

```text
Actor blocks circular credential dependency on a cloud vault
+-- Starting context: vault "contoso" uses credential entry "vault/contoso-token"
+-- Entry route: SetAsync, MoveAsync, namespace write, or AccessTokenSource.GetAccessTokenAsync
+-- Trigger: write targets ("vault", "contoso-token") with provider "contoso", or read finds it there
+-- Guard 1: Write-Time Prevention (SetAsync / SetAsAsync / MergeSettingsAsync / MoveAsync)
|   +-- Check if target (namespace, key) matches the configured credential for target provider "contoso"
|   +-- Match detected:
|   |   \-- Fail immediately with CircularCredential [Validation / Conflict] before calling the vault
|   \-- No match -> Proceed normally
+-- Guard 2: Read-Time Prevention (AccessTokenSource)
|   +-- Vault "contoso" asks IAccessTokenSource for a bearer token
|   +-- AccessTokenSource inspects entry ("vault", "contoso-token") in vault.json
|   +-- Entry's recorded provider is "contoso" (e.g. hand-edited JSON file):
|   |   \-- Fail immediately with CircularCredential before calling "contoso"
|   \-- Entry's provider is "oauth" -> Resolve and return bearer token normally
\-- Success: Circular dependency is refused immediately with a clear error and 0 vault calls
```

## Step Details

### Guard 1 & Guard 2: Why Both Write and Read Checks Exist
- If `"vault/contoso-token"` were stored inside `"contoso"`, opening `"contoso"` would require reading `"vault/contoso-token"` from `"contoso"`, which would require opening `"contoso"`—an infinite recursion.
- Guard 1 stops application API calls (`SetAsync`, `MoveAsync`, namespace writes) from ever placing the credential there.
- Guard 2 stops infinite recursion even if someone manually edited `vault.json` on disk to set `"provider": "contoso"` on `"contoso-token"`.

## Success

The circular operation is rejected immediately with `CircularCredential` naming both the provider (`"contoso"`) and the credential address (`"vault/contoso-token"`), with zero calls made to the vault or browser.

## Failure Cases

- `CircularCredential`: Returned/thrown whenever a provider's credential address is directed to resolve through that same provider.

## Consistency Check

- Unique coverage: Covers write-time and read-time `CircularCredential` protection for OAuth-authenticated cloud vaults.
- Closest related use cases: `UC-001` covers normal vault reads and writes; `F-202 UC-005` covers `AuthorizeProviderAsync` storing the vault credential under `"oauth"`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: attempting to store `"vault/contoso-token"` with `provider: "contoso"` throws a `CircularCredential` error with 0 vault reads/writes and 0 browser launches.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
