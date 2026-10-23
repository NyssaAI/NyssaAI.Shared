---
id: F-103
phase: 1
status: planned
---

# F-103 Application Registration, Startup Pipeline & Test Seams

## Summary

Registers and composes `IConfiguration` and `IConfigurationExtensions` (`AddNyssaConfiguration` in .NET; `ConfigurationClient.create` in TypeScript/Node.js), resolves default application identity and paths, executes the ordered startup sequence, and provides dependency-injection seams for isolated testing.

## User Need

Developers need a one-line registration experience with sensible defaults for production and clean substitution seams (`IKeyring`, `IBrowserLauncher`, clock/time provider, HTTP client, and vault factories) so automated tests never touch the developer's real `~/.nyssa-ai/config-manifest.json` or OS credential store.

## Use Cases

- [UC-001: Register and Bootstrap Configuration with Zero Setup](uc-001-register-and-bootstrap-configuration-with-zero-setup.md)
- [UC-002: Customize Identity Paths and Lock Timeouts](uc-002-customize-identity-paths-and-lock-timeouts.md)
- [UC-003: Replace External Dependencies in Automated Tests](uc-003-replace-external-dependencies-in-automated-tests.md)

## Scope

- Registration entry points (`AddNyssaConfiguration()` with defaults, `NyssaConfigurationOptions` instance, or configuration callback; `ConfigurationClient.create(options)` in TypeScript/Node.js).
- First-registration-wins semantics in dependency injection containers.
- Default option resolution:
  - `ApplicationName`: entry assembly `AssemblyTitle` / assembly name in .NET, or caller-supplied/package application name in Node.js (failing startup if blank or missing).
  - `ConfigDirectory`: `<AppContext.BaseDirectory>/.nyssa-ai/config` (or `<cwd>/.nyssa-ai/config`).
  - `Bootstrap.ConfigManifest`: `~/.nyssa-ai/config-manifest.json`.
  - `Bootstrap.LockTimeout`: 10 seconds.
- Ordered 6-step startup initialization pipeline upon first resolution:
  1. Validate options and application identity.
  2. Read or provision the application's manifest section (`F-002`) and apply manifest overrides (`configurationDirectory`, `lockTimeout`, `keyVault`).
  3. Verify and prepare the canonical configuration directory (`F-001`).
  4. Retrieve or generate the 256-bit local protection key in `IKeyring` (`F-003`), even if the application initially writes only plaintext settings.
  5. Instantiate built-in providers (`"aesLocal"`, `"oauth"`) and optional cloud vault adapter (`F-203`).
  6. Compose `ConfigurationStore` and `ConfigurationClient` for lazy per-namespace reads and writes.
- Test and host substitution seams for `IKeyring`, `IBrowserLauncher`, `TimeProvider` / clock, `IHttpClientFactory` / `fetch`, `IConfigurationLogger`, and `ICloudKeyVaultFactory`.

## Out of Scope

- Eagerly loading all `<namespace>.json` files at startup (namespaces are loaded on demand when requested).
- Concrete third-party vault SDK implementations.

## Inputs

- `NyssaConfigurationOptions` / `ConfigurationOptions` (`ApplicationName`, `ConfigDirectory`, `BootstrapOptions`).
- Optional host/test-registered services (`IKeyring`, `IBrowserLauncher`, `TimeProvider`, `IHttpClientFactory`, `ICloudKeyVaultFactory[]`, `IConfigurationLogger`).

## Outputs

- Initialized `IConfiguration` and `IConfigurationExtensions` client instances.
- Startup exception (`InvalidOperationException` / failed initialization `Result`) if the manifest is unsafe/invalid, the configuration directory fails safety checks, the OS keyring is unavailable, or a configured vault factory is missing.

## Acceptance

- Resolving `IConfiguration` with default options provisions the manifest section, prepares the configuration folder, and ensures the encryption key exists in `IKeyring`.
- Startup fails immediately if `IKeyring` is unavailable or if the manifest/configuration directory fails `DirectorySafety` checks.
- Tests can substitute fakes for `IKeyring`, `IBrowserLauncher`, clock, HTTP transport, and vault factories without touching the host machine's real `~/.nyssa-ai/config-manifest.json` or OS credential store.
- Both `.NET` and `TypeScript/Node.js` implementations pass setup and isolation test suites.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-001](../../features/f-001/feature.md)
- [F-002](../../features/f-002/feature.md)
- [F-003](../../features/f-003/feature.md)
- [F-101](../../features/f-101/feature.md)
- [F-102](../../features/f-102/feature.md)
