---
feature: F-002
id: UC-003
status: planned
---

# UC-003 Apply Manifest Overrides for Lock Timeout and Key Vault

## Actor

System Operator / Application Startup Pipeline (`ConfigurationManifestReader`, `ConfigurationServiceCollectionExtensions`, `ConfigurationBootstrap`, `ConfigurationClient`).

## Goal

Allow an operator to override the application's lock timeout in `applications.<app>` or configure shared cloud key vaults in root `keyVaults` of `config-manifest.json` without recompiling application code, while verifying that secret-bearing manifests are strictly private and all manifest vault secrets are AES-256-GCM sealed.

## Starting Context

An operator has configured `lockTimeoutMs` / `lockTimeout` inside `applications.<app>` and/or one or more vault blocks (`kind`, `endpoint`, `credential`, `cacheTTLSeconds`, `settings`, encrypted `secrets`) inside the root `keyVaults` map of `config-manifest.json`.

## Entry Route

Evaluated during `ConfigurationManifestReader.Read` / `ReadApplication` and multi-vault bootstrap layering at startup.

## Trigger

Startup reads `config-manifest.json` containing an application `lockTimeout` override and/or root `keyVaults` definitions.

## Process

```text
Actor applies operator overrides from manifest
+-- Starting context: manifest contains applications.<app>.lockTimeout and/or root keyVaults
+-- Entry route: Read / ReadApplication and multi-vault layering during startup
+-- Trigger: application resolves configuration client
+-- Step 1: Check private-file permissions FIRST when vault secrets are present
|   +-- Any root keyVaults.<name>.secrets (or legacy applications.<app>.keyVault.secrets) is non-empty
|   |   +-- Call DirectorySafety.VerifyPrivateFile(manifestPath) FIRST (F-001 UC-002)
|   |   \-- File readable by other accounts -> Fail startup immediately with Exposed [Forbidden]
|   \-- No vault secrets present -> Standard write-safety check is sufficient
+-- Step 2: Reject legacy per-app keyVault & validate root keyVaults
|   +-- applications.<app>.keyVault is present -> Fail closed with Validation
|   \-- Validate each root keyVaults.<name> entry and FormatVersion 2 encrypted aesLocal secret envelopes
+-- Step 3: Apply lockTimeout override
|   +-- Manifest specifies lockTimeoutMs / lockTimeout -> Override BootstrapOptions.LockTimeout
|   \-- Manifest omits lockTimeout -> Keep BootstrapOptions.LockTimeout (default 10s)
+-- Step 4: Unseal manifest vault secrets & layer root keyVaults over BootstrapOptions
|   +-- Root keyVaults has 1+ secret entries -> Read "nyssa-ai-configuration/_manifest" key (read-only) and unseal with AAD ("keyVaults/<name>", "<secretKey>")
|   +-- Root keyVaults has 0 secret entries -> Skip "_manifest" keyring lookup
|   \-- Layer root keyVaults over BootstrapOptions.KeyVault / KeyVaults by vault name (replacing same-named vault whole, keeping distinct vaults)
\-- Success: Startup proceeds with operator-configured lock timeout and layered multi-vault settings
```

## Step Details

### Step 4: Unseal manifest vault secrets & layer root `keyVaults`
- When root `keyVaults` defines a vault whose `name` matches a programmatic vault in `BootstrapOptions.KeyVault` or `BootstrapOptions.KeyVaults`, the manifest vault replaces that programmatic vault as a complete unit (inheriting programmatic `cacheTTLSeconds` only when omitted in the manifest), while programmatic vaults with distinct names remain active alongside manifest vaults.
- Every `keyVaults.<name>.secrets.<secretKey>` entry on disk must be an encrypted `aesLocal` FormatVersion 2 envelope sealed with `nyssa-ai-configuration/_manifest` and AAD `("keyVaults/<name>", "<secretKey>")`; plaintext secret strings and legacy `applications.<app>.keyVault` blocks fail closed with `Validation`.
- Nothing in this flow reads environment variables for vault configuration or provider selection.

## Success

The application uses the lock timeout and shared root `keyVaults` defined in `config-manifest.json`, unseals any encrypted vault bootstrap secrets via the `_manifest` master key, and verifies that secret-bearing manifests are unreadable by other users.

## Failure Cases

- `Exposed`: The manifest contains non-empty `keyVaults.<name>.secrets` (or legacy `applications.<app>.keyVault.secrets`) and is readable by group/other or multi-user Windows accounts (`ErrorCategory.Forbidden`).
- `Validation`: Legacy `applications.<app>.keyVault` is present; `lockTimeout` is zero, negative, or unparseable; a vault name is blank, reserved (`aesLocal`, `literal`, `oauth`, `default`, `plaintext`), or malformed; `kind` is blank; `endpoint` is not an absolute URI; `credential` is not a valid `namespace/key` address; a `secrets` entry is plaintext or malformed; or the `nyssa-ai-configuration/_manifest` key is missing when unsealing secrets.

## Consistency Check

- Unique coverage: Covers operator overrides for `lockTimeout` and root `keyVaults` in the manifest, mandatory `ManifestSecretEnvelope` validation/unsealing, multi-vault layering by `name`, and private-file verification when `secrets` is populated.
- Closest related use cases: `UC-002` covers `configurationDirectory` resolution; `F-001 UC-002` defines the OS permission check executed by `VerifyPrivateFile`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: manifest `lockTimeout` overrides code defaults, root `keyVaults` entries unseal via `_manifest` and layer over programmatic vaults by `name`, legacy `applications.<app>.keyVault` is rejected, and secret-bearing manifests require private file permissions.
- Failure cases produce clear guidance without logging secret values.
- The consistency check shows no unresolved duplicate coverage or contradictions.
