---
feature: F-203
id: UC-001
status: planned
---

# UC-001 Save and Read Secret Through a Cloud Key Vault

## Actor

Application Service (`IConfiguration.SetAsync`, `IConfiguration.GetAsync`, `CloudKeyVaultProvider`).

## Goal

Store a sensitive value in an external cloud key vault while keeping only a non-secret pointer (`ConfigurationPointer`) in the local `<namespace>.json` file, and read the secret back seamlessly through `IConfiguration`.

## Starting Context

A cloud key vault factory (`ICloudKeyVaultFactory`) is registered for the vault's `kind` (e.g. `"bitwarden-secrets"`), and the vault is configured under a provider name (e.g. `"mailVault"`) in `config-manifest.json` or `BootstrapOptions.KeyVault`.

## Entry Route

Called via `await configuration.SetAsync("mail", "ApiKey", secretValue, provider: "mailVault")` and `await configuration.GetAsync("mail", "ApiKey")`.

## Trigger

An application writes a secret specifying the configured vault provider name, or reads an entry whose recorded provider is that vault.

## Process

```text
Actor saves and reads a secret backed by a cloud key vault
+-- Starting context: vault provider "mailVault" registered and configured
+-- Entry route: IConfiguration.SetAsync or GetAsync with provider "mailVault"
+-- Trigger: application stores or retrieves a vault-backed secret
+-- Phase A: Writing Secret to Cloud Vault (SetAsync)
|   +-- Acquire <namespace>.json lock
|   +-- Call ICloudKeyVault.WriteAsync("mail", "ApiKey", secretValue)
|   |   +-- Vault write fails -> Abort without modifying <namespace>.json; throw ResultException
|   |   \-- Vault write succeeds -> Vault returns ConfigurationPointer (e.g. "mail/ApiKey")
|   \-- Save pointer in mail.json:
|       \-- {"value": "mail/ApiKey", "provider": "mailVault"} (encrypted is omitted)
+-- Phase B: Reading Secret from Cloud Vault (GetAsync)
|   +-- Read mail.json and see entry has "provider": "mailVault" and pointer "mail/ApiKey"
|   +-- Parse ConfigurationPointer("mailVault", "mail/ApiKey")
|   +-- Call ICloudKeyVault.ReadAsync(pointer)
|   |   +-- Secret missing in vault -> Throw ResultException [NotFound]
|   |   +-- Vault unreachable or unauthorized -> Throw ResultException [Unavailable / Forbidden]
|   |   \-- Vault returns secret string -> Return secret to caller
\-- Success: Secret stays in remote vault; local file holds only the pointer
```

## Step Details

### Phase A: Writing Secret to Cloud Vault
- The local `<namespace>.json` file never contains the secret or ciphertext; it holds only the pointer string returned by the vault adapter and `"provider": "mailVault"`.
- Because the local pointer is an address rather than ciphertext, `"encrypted"` is omitted from the JSON entry.

## Success

The secret is stored in the remote cloud vault, `<namespace>.json` records only the `ConfigurationPointer`, and `GetAsync` returns the secret value transparently.

## Failure Cases

- `Validation`: The named vault provider is not configured, or no `ICloudKeyVaultFactory` matching `keyVault.kind` was registered at startup.
- `NotFound`: The local pointer exists in `<namespace>.json`, but the secret was deleted directly in the remote vault.
- `Forbidden` / `Unavailable`: The vault rejected credentials or could not be reached.

## Consistency Check

- Unique coverage: Covers single-key read and write through an `ICloudKeyVault` adapter and local `ConfigurationPointer` storage.
- Closest related use cases: `UC-002` combines vault entries with plaintext and `"aesLocal"` entries in a single DTO; `UC-003` blocks circular vault credentials.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: `<namespace>.json` stores `{"value": "<pointer>", "provider": "<vaultName>"}` without the secret text, and `GetAsync` returns the remote secret.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
