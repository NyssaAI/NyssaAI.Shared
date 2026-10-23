---
feature: F-103
id: UC-002
status: planned
---

# UC-002 Customize Identity Paths and Lock Timeouts

## Actor

Application Developer or Systems Architect (`NyssaConfigurationOptions`).

## Goal

Configure custom application names, explicit configuration directories, custom manifest locations, and custom lock timeouts during registration.

## Starting Context

The application is booting up and requires specific directory locations (e.g. for container mounts or CLI subcommands).

## Entry Route

Called by passing an options object or callback to `AddNyssaConfiguration(options => { ... })` or `ConfigurationClient.create(options)`.

## Trigger

An application requires non-default paths or timing parameters.

## Process

```text
Actor configures custom registration options
+-- Starting context: custom requirements known (paths, name, timeout)
+-- Entry route: AddNyssaConfiguration(options => { ... })
+-- Trigger: application service setup
+-- Step 1: Supply custom options
|   +-- options.ApplicationName = "CustomCli"
|   +-- options.ConfigDirectory = "/var/data/custom-app"
|   +-- options.Bootstrap.ConfigManifest = "/etc/custom/manifest.json"
|   \-- options.Bootstrap.LockTimeout = TimeSpan.FromSeconds(5)
+-- Step 2: Validate options
|   \-- Ensure ApplicationName is not empty; LockTimeout is positive
+-- Step 3: Layer options during bootstrap
|   +-- Manifest section is looked up at /etc/custom/manifest.json
|   \-- If manifest already records a configurationDirectory, that path wins
\-- Success: Service initialized with custom configuration parameters
```

## Step Details

### Step 3: Layer options during bootstrap
- If the custom manifest already contains an entry for `CustomCli`, the `configurationDirectory` recorded in that file is canonical and wins over `options.ConfigDirectory`. This allows operators to redirect paths via manifest without breaking application code.

## Success

The library initializes using the specified custom directories, manifest path, and timeouts.

## Failure Cases

- `Validation`: Lock timeout is zero or negative, or application name is blank.
- `Forbidden`: Custom directory or manifest fails security verification.

## Consistency Check

- Unique coverage: Covers developer configuration options and overrides.
- Closest related use cases: `UC-001` uses defaults; `UC-003` overrides test dependencies.
- Contradictions: None found.

## Acceptance

- Custom manifest path and directory paths are respected.
- Custom lock timeout is applied to lock operations.
