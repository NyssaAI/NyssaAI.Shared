---
id: F-002
phase: 0
status: planned
---

# F-002 Configuration Manifest & First-Use Provisioning

## Summary

Owns the schema, parsing, validation, and atomic first-use provisioning of `~/.nyssa-ai/config-manifest.json` (or an explicitly configured manifest path), settling each application's canonical `configurationDirectory`, `lockTimeout`, and opt-in shared namespace files, as well as shared root-level `keyVaults` definitions and their encrypted bootstrap secret envelopes.

## User Need

Applications need zero-ceremony default storage discovery on first run, while operators need a single canonical file (`config-manifest.json`) where they can redirect an application's configuration directory, adjust lock timeouts, selectively route named namespaces to shared files, or configure shared cloud key vaults (`keyVaults`) with AES-256-GCM encrypted bootstrap secrets without changing application code.

## Use Cases

- [UC-001: Provision Missing Manifest Section on First Use](uc-001-provision-missing-manifest-section-on-first-use.md)
- [UC-002: Resolve Canonical Configuration Directory](uc-002-resolve-canonical-configuration-directory.md)
- [UC-003: Apply Manifest Overrides for Lock Timeout and Key Vault](uc-003-apply-manifest-overrides-for-timeout-and-vault.md)
- [UC-004: Select Registered Shared Namespace Files](uc-004-select-registered-shared-namespace-files.md)

## Scope

- Reading and validating schema `version: 1` manifest files (`ConfigurationManifestReader`), including root `keyVaults` (`VaultManifestEntry`) and per-application `applications` (`ApplicationManifestEntry`), while rejecting legacy per-application `applications.<app>.keyVault` blocks with `Validation`.
- Resolving the application's section by `ApplicationName` and converting relative `configurationDirectory` paths against the manifest file's parent directory.
- Reading the optional root `namespaceRegistry`, keyed by namespace name, and resolving registered namespace file paths relative to the manifest file's parent directory.
- Validating each application's optional `sharedNamespaces` list against the root registry; routing opted-in namespaces to their registered file and all other namespaces to the application's existing `<configurationDirectory>/<namespace>.json` location.
- Deriving the shared document's sidecar lock path from its resolved file path so opted-in applications coordinate access to the same file. Registry routing is not an OS-level access-control boundary.
- Keeping encryption-key ownership separate from file routing: an `aesLocal` entry written with one application's key is not readable by another application's distinct key unless shared-key behavior is separately defined and implemented.
- Enforcing `DirectorySafety.VerifyPrivateFile` **first** whenever any root `keyVaults.<vaultName>.secrets` (or legacy `applications.<app>.keyVault.secrets`) map is non-empty, before running legacy-section or envelope validation.
- Requiring every `keyVaults.<vaultName>.secrets.<secretKey>` entry on disk to be a FormatVersion 2 AES-256-GCM `ManifestSecretEnvelope` (`encrypted: true`, `provider: "aesLocal"`) sealed under the shared manifest master key (`service = "nyssa-ai-configuration"`, `account = "_manifest"`) with AAD `("keyVaults/<vaultName>", "<secretKey>")`, rejecting plaintext values with `Validation`.
- Distinguishing between a missing manifest/section (`NotFound`) and an unreadable or invalid manifest (`Invalid` / `Forbidden` / I/O failure) so startup never guesses a fallback path on permission errors.
- Atomically provisioning a missing manifest file or missing application section, and atomically setting or removing root `keyVaults` entries (`ConfigurationManifestWriter`) under a sidecar `<manifest>.lock` file and temporary-file rename while preserving sibling `applications`, `keyVaults`, and `namespaceRegistry` entries, including each application's `sharedNamespaces`, and omitting empty optional maps and redundant inner `name` fields.
- Layering manifest settings over developer `BootstrapOptions`: a recorded `configurationDirectory` is canonical; a manifest `lockTimeout` overrides code `LockTimeout`; root `keyVaults` entries layer over programmatic `KeyVault` / `KeyVaults` by `vault.name` (replacing a same-named programmatic vault whole while preserving code `cacheTTLSeconds` only when omitted in the manifest, and allowing distinct vault names to coexist).
- Refusing to read process environment variables for manifest discovery or provider selection.

## Out of Scope

- Reading or writing `<namespace>.json` configuration entries (owned by `F-101`).
- Instantiating cloud vault adapters or OAuth providers from the resolved `VaultOptions` (owned by `F-103` and `F-203`).

## Inputs

- Manifest file path (defaults to `~/.nyssa-ai/config-manifest.json`).
- Application name (`ApplicationName`) and initial default/suggested `ConfigDirectory`.
- Developer-supplied `BootstrapOptions` (`ConfigManifest`, `LockTimeout`, `KeyVault`, `KeyVaults`).

## Outputs

- Parsed `ConfigurationManifest` (including root `keyVaults` and `namespaceRegistry`) and resolved application manifest entry containing the canonical absolute `configurationDirectory`, effective `lockTimeout`, selected namespace file paths, and layered `VaultOptions[]` (`name`, `kind`, `endpoint`, `credential`, `cacheTTLSeconds`, `settings`, `secrets`).
- Provisioned or updated `config-manifest.json` on disk when the file, application section, or root `keyVaults` entry is created or updated.

## Acceptance

- Missing manifest or missing application section is created atomically on first resolution using the initial `ConfigDirectory`, omitting `"keyVaults"` when empty and preserving existing root `keyVaults`, `namespaceRegistry`, and per-application `sharedNamespaces` when present.
- Once recorded in the manifest, the application's `configurationDirectory` takes precedence over subsequent changes to `options.ConfigDirectory` in code.
- Relative `configurationDirectory` values resolve relative to the manifest's directory.
- A namespace listed in an application's `sharedNamespaces` resolves to the corresponding root `namespaceRegistry` file; an unlisted namespace retains the existing application-directory file path.
- Relative `namespaceRegistry` file paths resolve relative to the manifest's directory, independent of the application's `configurationDirectory`; absolute paths remain absolute.
- An application cannot opt into a namespace absent from `namespaceRegistry`; such a manifest fails validation rather than silently falling back.
- Shared file routing does not imply shared `aesLocal` decryption keys; shared encrypted namespace behavior must be explicitly specified before implementation.
- Manifest `lockTimeout` overrides developer `BootstrapOptions`, root `keyVaults` entries layer over programmatic `KeyVault` / `KeyVaults` by `vault.name`, and legacy `applications.<app>.keyVault` fails closed with `Validation`.
- Manifests carrying `keyVaults.<vaultName>.secrets` (or legacy `keyVault.secrets`) fail startup first if readable by other users, and plaintext secret values in `config-manifest.json` are rejected.
- `.NET` (`NyssaAI.Configuration`), `TypeScript/Node.js` (`@nyssa-ai/configuration`), and `Python` (`nyssa-ai-configuration`) implementations pass all manifest unit and tutorial use-case tests.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-001](../../features/f-001/feature.md)
