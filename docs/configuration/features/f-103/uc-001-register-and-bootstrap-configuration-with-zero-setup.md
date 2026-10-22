---
feature: F-103
id: UC-001
status: planned
---

# UC-001 Register and Bootstrap Configuration with Zero Setup

## Actor

Application Developer / Main Program Entrypoint (`services.AddNyssaConfiguration()`, `ConfigurationClient.create()`).

## Goal

Enable full configuration and secret management in an application using a single registration call with sensible production defaults.

## Starting Context

The application is booting up and setting up its dependency injection container or services.

## Entry Route

Called via `services.AddNyssaConfiguration()` in .NET or `await ConfigurationClient.create()` in TypeScript/Node.js.

## Trigger

Application startup code configures services.

## Process

```text
Actor registers and bootstraps configuration library
+-- Starting context: application booting up, default options used
+-- Entry route: AddNyssaConfiguration() or ConfigurationClient.create()
+-- Trigger: application service registration
+-- Step 1: Detect Application Identity
|   \-- Determine ApplicationName from entry assembly title/name (or package name)
+-- Step 2: Establish Default Paths
|   +-- Manifest: ~/.nyssa-ai/config-manifest.json
|   \-- Config directory default: <appBaseDir>/.nyssa-ai/config
+-- Step 3: Execute 6-Step Startup Sequence on first service resolution
|   +-- 1. Read or create application section in config-manifest.json
|   +-- 2. Apply manifest overrides (directory, lock timeout, vault)
|   +-- 3. Verify and prepare configuration directory with restricted permissions
|   +-- 4. Load or generate 32-byte master key in OS keychain
|   +-- 5. Prepare providers ("aesLocal", "oauth", and optional vault)
|   \-- 6. Make IConfiguration available for injection
\-- Success: IConfiguration is ready to inject and use across the entire application
```

## Step Details

### Step 3: Execute 6-Step Startup Sequence
- Initialization is deferred until `IConfiguration` is first resolved.
- Everything runs automatically: if the manifest or configuration directory is missing, it is created. If the master key is missing, it is generated in the OS keychain. The developer doesn't need to write file creation or keychain code.

## Success

`IConfiguration` is registered and ready to use across all application classes.

## Failure Cases

- `InvalidOperation`: Application name cannot be determined.
- `Forbidden`: Configuration folder or manifest fails safety permissions checks.
- `Unexpected`: OS credential store is unavailable at startup.

## Consistency Check

- Unique coverage: Covers zero-config registration and default bootstrap sequence.
- Closest related use cases: `UC-002` covers overriding options; `UC-003` covers replacing dependencies in tests.
- Contradictions: None found.

## Acceptance

- A single line of code sets up configuration.
- Missing folders and manifest sections are created automatically.
- Keyring is initialized on first resolution.
