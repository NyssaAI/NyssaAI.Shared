---
id: F-203
phase: 2
status: planned
---

# F-203 Cloud Key Vault Adaptation

## Summary

Connects external secret vault packages (`ICloudKeyVaultFactory` and `ICloudKeyVault`) to `ConfigurationStore` so secrets remain in the remote vault while `<namespace>.json` stores only a `ConfigurationPointer`, supporting both static bootstrap secrets and OAuth bearer token authentication (`IAccessTokenSource`).

## User Need

Enterprise and team applications need to keep shared or regulated secrets in a remote key vault (such as Bitwarden Secrets Manager, Azure Key Vault, or HashiCorp Vault) while application modules read and write them through the exact same `IConfiguration` methods used for local settings.

## Use Cases

- [UC-001: Save and Read Secret Through a Cloud Key Vault](uc-001-save-and-read-secret-in-cloud-vault.md)
- [UC-002: Assemble a Multi-Provider Settings Object](uc-002-assemble-multi-provider-settings-object.md)
- [UC-003: Block Circular Vault Credential Storage](uc-003-block-circular-vault-credential.md)

## Scope

- Public vault contracts in `Contracts`:
  - `ICloudKeyVaultFactory`: declares `Kind`, creates an `ICloudKeyVault` instance from `VaultOptions` and optional `IAccessTokenSource`, and optionally supplies `OAuthOptions` via `Authorization(VaultOptions)` when the vault authenticates with OAuth2.
  - `ICloudKeyVault`: implements `ReadAsync(ConfigurationPointer)`, `WriteAsync(namespace, key, value)`, and `RemoveAsync(ConfigurationPointer)` returning `Result<ConfigurationRemoval>`.
  - `ConfigurationPointer`: represents the local reference stored in `<namespace>.json` (`value: "<pointer>"`, `provider: "<vaultName>"`, with `encrypted` omitted because the local pointer itself is not ciphertext).
- `AccessTokenSource` (`IAccessTokenSource`):
  - Supplied to vault factories when `VaultOptions.Credential` names a local `namespace/key` entry.
  - Resolves the bearer token through `ConfigurationStore` (automatically leveraging `F-202` token refresh and locking) without exposing refresh tokens or encryption keys to the vault package.
- Circular credential protection (`CircularCredential`):
  - Refuses writes that attempt to store a vault's configured `credential` entry inside that same vault provider, and refuses token-source reads if the credential entry resolves through the vault it authenticates.
- Static secret delivery via `VaultOptions.Secrets` (with redaction so secret values are never logged).

## Out of Scope

- Bundling third-party cloud vault SDKs inside the core configuration package.
- Guarded two-phase recovery preparation/reconciliation (`ICloudKeyVaultRecovery`, owned by `F-204`).

## Inputs

- Registered `ICloudKeyVaultFactory` instances matched by `VaultOptions.Kind`.
- Resolved `VaultOptions` (`Name`, `Kind`, `Endpoint`, `Credential`, `Settings`, `Secrets`) from `BootstrapOptions` or `config-manifest.json`.

## Outputs

- Internal `IConfigurationProvider` adapter registered under `VaultOptions.Name`.
- Local `ConfigurationPointer` entries in `<namespace>.json` and remote secret reads/writes/deletions delegated to `ICloudKeyVault`.

## Acceptance

- Saving a value with `provider: "<vaultName>"` writes the secret to `ICloudKeyVault.WriteAsync` and records only the returned `ConfigurationPointer` and `"provider": "<vaultName>"` in `<namespace>.json`.
- Reading that entry resolves the secret through `ICloudKeyVault.ReadAsync(pointer)`, including when assembled as part of a multi-provider namespace DTO (`GetAsAsync<T>(namespace)`).
- Configuring a vault with an OAuth `credential` (`"ns/key"`) supplies a working `IAccessTokenSource` to the vault factory and blocks `CircularCredential` self-reference on both read and write.
- Both `.NET` and `TypeScript/Node.js` implementations support pluggable cloud vault factories and pass multi-provider namespace tutorials.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-002](../../features/f-002/feature.md)
- [F-101](../../features/f-101/feature.md)
- [F-103](../../features/f-103/feature.md)
- [F-202](../../features/f-202/feature.md)
