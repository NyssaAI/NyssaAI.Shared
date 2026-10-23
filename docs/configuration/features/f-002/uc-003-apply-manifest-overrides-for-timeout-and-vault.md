---
feature: F-002
id: UC-003
status: planned
---

# UC-003 Apply Manifest Overrides for Lock Timeout and Key Vault

## Actor

System Operator / Application Startup Pipeline (`ConfigurationManifestReader`, `ConfigurationServiceCollectionExtensions`).

## Goal

Allow an operator to override the application's lock timeout or configure a cloud key vault in `config-manifest.json` without recompiling application code, while verifying that secret-bearing manifests are strictly private.

## Starting Context

An operator has configured `lockTimeout` and/or a `keyVault` block (`name`, `kind`, `endpoint`, `credential`, `settings`, `secrets`) inside the application's section of `config-manifest.json`.

## Entry Route

Evaluated during `ConfigurationManifestReader.ReadApplication` and `OverlayKeyVault` at startup.

## Trigger

Startup reads an application manifest section that includes `lockTimeout` or `keyVault`.

## Process

```text
Actor applies operator overrides from manifest section
+-- Starting context: manifest section contains lockTimeout and/or keyVault
+-- Entry route: ReadApplication and OverlayKeyVault during startup
+-- Trigger: application resolves IConfiguration
+-- Step 1: Check private-file permissions when vault secrets are present
|   +-- keyVault.secrets contains one or more entries
|   |   +-- Call DirectorySafety.VerifyPrivateFile(manifestPath) (F-001 UC-002)
|   |   \-- File readable by other accounts -> Fail startup with Exposed [Forbidden]
|   \-- keyVault.secrets is empty or absent -> Standard write-safety check is sufficient
+-- Step 2: Apply lockTimeout override
|   +-- Manifest specifies lockTimeout -> Parse positive TimeSpan and override BootstrapOptions.LockTimeout
|   \-- Manifest omits lockTimeout -> Keep BootstrapOptions.LockTimeout (default 10s)
+-- Step 3: Apply keyVault override
|   +-- Manifest specifies keyVault:
|   |   +-- Validate name, kind, optional absolute endpoint URI, and optional "namespace/key" credential
|   |   \-- Replace developer's BootstrapOptions.KeyVault in full with manifest keyVault
|   \-- Manifest omits keyVault -> Keep developer's BootstrapOptions.KeyVault (if any)
\-- Success: Startup proceeds with operator-configured lock timeout and key vault settings
```

## Step Details

### Step 3: Apply `keyVault` override
- When a manifest section defines `keyVault`, it replaces `BootstrapOptions.KeyVault` as a complete unit rather than merging individual fields, ensuring an operator who redirects an app to a production vault does not inherit leftover settings or credentials from developer defaults.
- Nothing in this flow reads environment variables.

## Success

The application uses the lock timeout and cloud key vault defined in `config-manifest.json`, and any manifest carrying static vault secrets is verified to be unreadable by other users.

## Failure Cases

- `Exposed`: The manifest contains `keyVault.secrets` and is readable by group/other or multi-user Windows accounts (`ErrorCategory.Forbidden`).
- `Validation`: `lockTimeout` is zero, negative, or unparseable; `keyVault.name` or `keyVault.kind` is blank; `keyVault.endpoint` is not an absolute URI; or `keyVault.credential` is not a valid `namespace/key` address.

## Consistency Check

- Unique coverage: Covers operator overrides for `lockTimeout` and `keyVault` in the manifest and triggers private-file verification when `keyVault.secrets` is populated.
- Closest related use cases: `UC-002` covers `configurationDirectory` resolution; `F-001 UC-002` defines the OS permission check executed by `VerifyPrivateFile`.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: manifest `lockTimeout` and `keyVault` replace code defaults, and secret-bearing manifests require private file permissions.
- Failure cases produce clear guidance without logging secret values.
- The consistency check shows no unresolved duplicate coverage or contradictions.
