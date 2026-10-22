---
id: F-002
phase: 0
status: planned
---

# F-002 Configuration Manifest & First-Use Provisioning

## Summary

Owns the schema, parsing, validation, and atomic first-use provisioning of `~/.nyssa-ai/config-manifest.json` (or an explicitly configured manifest path), settling the canonical `configurationDirectory`, `lockTimeout`, and `keyVault` configuration for each application.

## User Need

Applications need zero-ceremony default storage discovery on first run, while operators need a single canonical file (`config-manifest.json`) where they can redirect an application's configuration directory, adjust lock timeouts, or bind a cloud key vault without changing application code.

## Use Cases

- [UC-001: Provision Missing Manifest Section on First Use](uc-001-provision-missing-manifest-section-on-first-use.md)
- [UC-002: Resolve Canonical Configuration Directory](uc-002-resolve-canonical-configuration-directory.md)
- [UC-003: Apply Manifest Overrides for Lock Timeout and Key Vault](uc-003-apply-manifest-overrides-for-timeout-and-vault.md)

## Scope

- Reading and validating schema `version: 1` manifest files (`ConfigurationManifestReader`).
- Resolving the application's section by `ApplicationName` and converting relative `configurationDirectory` paths against the manifest file's parent directory.
- Enforcing `DirectorySafety.VerifyPrivateFile` whenever an application's `keyVault.secrets` map is non-empty.
- Distinguishing between a missing manifest/section (`NotFound`) and an unreadable or invalid manifest (`Invalid` / `Forbidden` / I/O failure) so startup never guesses a fallback path on permission errors.
- Atomically provisioning a missing manifest file or missing application section (`ConfigurationManifestWriter`) under a sidecar `<manifest>.lock` file and temporary-file rename while preserving all other application sections and fields verbatim.
- Layering manifest settings over developer `BootstrapOptions`: a recorded `configurationDirectory` is canonical; a manifest `lockTimeout` overrides code `LockTimeout`; a manifest `keyVault` replaces code `KeyVault` in full.
- Refusing to read process environment variables for manifest discovery or provider selection.

## Out of Scope

- Reading or writing `<namespace>.json` configuration entries (owned by `F-101`).
- Instantiating cloud vault adapters or OAuth providers from the resolved `VaultOptions` (owned by `F-103` and `F-203`).

## Inputs

- Manifest file path (defaults to `~/.nyssa-ai/config-manifest.json`).
- Application name (`ApplicationName`) and initial default/suggested `ConfigDirectory`.
- Developer-supplied `BootstrapOptions` (`ConfigManifest`, `LockTimeout`, `KeyVault`).

## Outputs

- Resolved application manifest entry containing the canonical absolute `configurationDirectory`, effective `lockTimeout`, and optional `VaultOptions` (`name`, `kind`, `endpoint`, `credential`, `settings`, `secrets`).
- Provisioned or updated `config-manifest.json` on disk when the file or application section was absent on first use.

## Acceptance

- Missing manifest or missing application section is created atomically on first resolution using the initial `ConfigDirectory`.
- Once recorded in the manifest, the application's `configurationDirectory` takes precedence over subsequent changes to `options.ConfigDirectory` in code.
- Relative `configurationDirectory` values resolve relative to the manifest's directory.
- Manifest `lockTimeout` and `keyVault` definitions override developer `BootstrapOptions`.
- Manifests carrying `keyVault.secrets` fail startup if readable by other users.
- Both `.NET` (`NyssaAI.Configuration`) and `TypeScript/Node.js` (`@nyssa-ai/configuration`) implementations pass all manifest unit and tutorial use-case tests.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-001](../../features/f-001/feature.md)
