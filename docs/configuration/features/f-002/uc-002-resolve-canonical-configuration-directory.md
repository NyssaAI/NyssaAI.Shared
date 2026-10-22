---
feature: F-002
id: UC-002
status: planned
---

# UC-002 Resolve Canonical Configuration Directory

## Actor

Application Startup Pipeline (`ConfigurationManifestReader.ReadApplication`).

## Goal

Determine the single canonical folder where the application's `<namespace>.json` files live by reading the application's section in `config-manifest.json`, resolving relative paths against the manifest's folder, and ignoring later code changes to `options.ConfigDirectory`.

## Starting Context

`config-manifest.json` exists and contains a section for `ApplicationName` with a `configurationDirectory` value (either absolute or relative).

## Entry Route

Called via `ConfigurationManifestReader.ReadApplication(manifestPath, applicationName)` during startup.

## Trigger

Application startup resolves `IConfiguration`.

## Process

```text
Actor resolves canonical configuration directory from manifest
+-- Starting context: manifest file exists with a section for ApplicationName
+-- Entry route: ConfigurationManifestReader.ReadApplication(manifestPath, applicationName)
+-- Trigger: startup reads manifest before opening ConfigurationStore
+-- Step 1: Verify manifest file safety
|   +-- Call DirectorySafety.Verify(manifestDirectory) and VerifyFile(manifestPath)
|   \-- Unsafe permissions -> Fail with Forbidden before reading
+-- Step 2: Parse manifest and locate application section
|   +-- Validate version == 1 and non-empty configurationDirectory
|   \-- Missing or blank configurationDirectory -> Fail with NotFound / Validation
+-- Step 3: Resolve configurationDirectory path
|   +-- Path is absolute (e.g. "D:/apps/orders/config")
|   |   \-- Keep exact absolute path
|   \-- Path is relative (e.g. "./data" or "OrdersApp")
|       \-- Resolve relative to the parent directory of config-manifest.json
+-- Step 4: Enforce canonical precedence over application code
|   \-- Use the manifest's resolved configurationDirectory even if options.ConfigDirectory points elsewhere
\-- Success: Canonical absolute configurationDirectory returned to startup pipeline
```

## Step Details

### Step 3: Resolve `configurationDirectory` path
- Relative paths in `configurationDirectory` are always resolved relative to the folder containing `config-manifest.json` (never the process's current working directory), so the manifest means the same location regardless of where the user launches the CLI from.

### Step 4: Enforce canonical precedence over application code
- Once `configurationDirectory` is recorded in the manifest, the manifest is the source of truth. Changing `options.ConfigDirectory` in application code on a later run does not move existing files or override the manifest's recorded path.

## Success

Startup obtains the exact, canonical absolute directory path where all `<namespace>.json` files for this application are stored.

## Failure Cases

- `NotFound`: The manifest file, application section, or `configurationDirectory` is missing.
- `Validation`: The manifest JSON is malformed or `version` is not `1`.
- `Forbidden`: The manifest file or its parent directory is writable by other local accounts.

## Consistency Check

- Unique coverage: Covers reading `configurationDirectory`, relative path resolution against the manifest directory, and canonical precedence over code options.
- Closest related use cases: `UC-001` provisions the section when missing; `UC-003` applies optional `lockTimeout` and `keyVault` overrides.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: relative `configurationDirectory` resolves against the manifest's parent folder, and recorded manifest paths win over `options.ConfigDirectory`.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
