---
feature: F-002
id: UC-001
status: planned
---

# UC-001 Provision Missing Manifest Section on First Use

## Actor

Application Startup Pipeline (`ConfigurationManifestReader`, `ConfigurationManifestWriter`).

## Goal

Automatically create `~/.nyssa-ai/config-manifest.json` (or add the current application's section to an existing manifest) on first run without disturbing any other application's settings in the same manifest file.

## Starting Context

The application is starting for the first time on a machine. Either `config-manifest.json` does not exist yet, or it exists for other tools but has no entry for `ApplicationName`.

## Entry Route

Triggered during `AddNyssaConfiguration()` / `ConfigurationClient.create()` when `ConfigurationManifestReader.ReadApplication` returns `NotFound`.

## Trigger

First resolution of `IConfiguration` when the application's manifest section is absent.

## Process

```text
Actor provisions missing application section in manifest
+-- Starting context: manifest file or application section is absent
+-- Entry route: ConfigurationManifestWriter.SetConfigurationDirectory called
+-- Trigger: ReadApplication returns NotFound during startup
+-- Step 1: Acquire sidecar manifest lock
|   +-- Verify parent directory safety (F-001)
|   \-- Acquire exclusive lock on <manifestPath>.lock (bounded by lockTimeout)
+-- Step 2: Read existing manifest under lock
|   +-- File does not exist yet -> Create new ConfigurationManifest (version: 1)
|   +-- File exists -> Parse existing sections so other applications are preserved
|   \-- File exists but is unreadable or malformed -> Fail closed (do not overwrite)
+-- Step 3: Record application's configurationDirectory
|   +-- Set applications[ApplicationName].configurationDirectory = options.ConfigDirectory
|   \-- Preserve all other fields and all other application sections verbatim
+-- Step 4: Publish updated manifest atomically
|   +-- Write JSON to temporary file <manifestPath>.tmp.<guid> with 0600 permissions
|   +-- Rename temporary file over <manifestPath>
|   \-- Release <manifestPath>.lock
\-- Success: Application section is recorded in config-manifest.json
```

## Step Details

### Step 2: Read existing manifest under lock
- Two different NyssaAI tools starting at the exact same moment coordinate through `<manifestPath>.lock`. Reading the manifest inside the lock ensures the second tool adds its section alongside the first tool's section rather than overwriting it.
- Only a confirmed missing file or missing section triggers provisioning. If the file exists on disk but fails permission checks or contains invalid JSON, startup fails immediately so operator data is never overwritten.

## Success

`config-manifest.json` exists on disk with `version: 1` and records the application's `configurationDirectory`, while leaving any other application sections untouched.

## Failure Cases

- `LockNotAcquired`: Another process held `<manifestPath>.lock` longer than `lockTimeout` (`ErrorCategory.Unavailable`).
- `Forbidden`: The manifest directory or file failed `DirectorySafety` checks.
- `Validation`: An existing `config-manifest.json` file contains malformed JSON or an unsupported schema version.

## Consistency Check

- Unique coverage: Covers first-use creation and atomic updates of the application's section in `config-manifest.json`.
- Closest related use cases: `UC-002` reads an existing section and resolves its directory; `UC-003` layers manifest overrides for lock timeouts and vaults.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: missing manifest or section is created atomically while preserving sibling application sections.
- Failure cases produce clear guidance without overwriting corrupt or unreadable manifests.
- The consistency check shows no unresolved duplicate coverage or contradictions.
