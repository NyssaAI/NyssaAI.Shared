---
feature: F-002
id: UC-004
status: planned
---

# UC-004 Select Registered Shared Namespace Files

## Actor

Application Startup Pipeline and Namespace Document Store (`ConfigurationManifestReader`, `ConfigurationDocumentFactoryLocalFile`).

## Goal

Allow an application to opt into selected root-registered namespace files while keeping every unselected namespace in its own `configurationDirectory`.

## Starting Context

`config-manifest.json` may contain a root `namespaceRegistry` mapping namespace names to file paths. An application may list the registered namespace names it uses in `applications.<app>.sharedNamespaces`.

## Entry Route

The startup pipeline resolves the application's manifest settings before opening namespace documents.

## Trigger

An application starts and requests a namespace document.

## Process

```text
Actor resolves the file for a namespace
+-- Starting context: the manifest and application name are known
+-- Entry route: ReadApplication(manifestPath, applicationName)
+-- Trigger: the application opens a namespace document
+-- Step 1: Parse the optional root namespaceRegistry
|   +-- Each key is a namespace name and each value is a file path
|   +-- Resolve relative file paths against the directory containing config-manifest.json
|   \-- Keep absolute file paths absolute
+-- Step 2: Validate applications.<app>.sharedNamespaces
|   +-- Each listed namespace must exist in namespaceRegistry
|   \-- Unknown namespace -> Fail with Validation
+-- Step 3: Resolve the requested namespace file
|   +-- Namespace is listed by the application
|   |   \-- Use its resolved namespaceRegistry file path
|   \-- Namespace is not listed
|       \-- Use <configurationDirectory>/<namespace>.json
+-- Step 4: Coordinate access to a shared file
|   \-- Derive the sidecar lock path from the resolved document file path
\-- Success: Namespace operations use the selected document path
```

## Step Details

### Step 1: Resolve registry file paths

- Registry paths are relative to the directory containing `config-manifest.json`, not the process working directory or an application's `configurationDirectory`. This makes a registered path resolve consistently for every application.
- Applications selecting the same registry namespace use the same resolved file and adjacent lock file.

### Step 3: Preserve private namespace defaults

- `namespaceRegistry` and `applications.<app>.sharedNamespaces` are optional. If absent, existing manifests continue to store every namespace under the application's `configurationDirectory`.
- A registered namespace is not shared with an application unless that application's `sharedNamespaces` lists it.
- Registry selection controls library file routing; it does not create an operating-system access-control boundary between processes running as the same user.

### Encryption boundary

- File routing alone does not make `aesLocal` entries decryptable across applications because local encryption keys are application-specific. Cross-application encrypted namespace support requires an explicit shared-key design; until then, the manifest must not imply that selecting a registry entry grants decryption.

## Success

An opted-in namespace resolves to its registered file, while all unlisted namespaces retain the application's canonical `configurationDirectory` behavior. Existing manifests without registry fields remain compatible.

## Failure Cases

- `Validation`: `sharedNamespaces` is malformed or names a namespace absent from the root `namespaceRegistry`.
- `Validation`: A registered namespace name or file path is invalid.
- `Forbidden` / I/O failure: The resolved manifest or namespace file cannot be accessed under the existing file-safety contract.

## Consistency Check

- Unique coverage: Covers opt-in namespace file selection, manifest-relative registry paths, and fallback to application-local namespace files.
- Closest related use cases: `UC-002` covers canonical `configurationDirectory` resolution; F-101 owns namespace document operations and locking; `UC-003` covers root manifest settings and validation.
- Contradictions: Shared `aesLocal` key ownership remains an explicit design prerequisite for cross-application decryption.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Registry file paths resolve relative to the manifest directory, regardless of the calling application's configuration directory or current working directory.
- Applications use only the shared namespace files they explicitly list; other namespaces use their application-local files.
- An unknown opted-in namespace fails with `Validation`.
- Existing manifests without `namespaceRegistry` or `sharedNamespaces` retain current behavior.
- Tests cover manifest parsing and path selection across `.NET`, TypeScript/Node.js, and Python implementations.
- Shared `aesLocal` entries are not described as cross-application readable until shared-key behavior is specified and implemented.
