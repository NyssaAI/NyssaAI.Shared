---
feature: F-103
id: UC-003
status: planned
---

# UC-003 Replace External Dependencies in Automated Tests

## Actor

Automated Test Suite / Integration Test Runner.

## Goal

Run comprehensive configuration and secret management tests in isolated environments by replacing OS keychains, system clocks, browser launchers, and HTTP clients with test doubles so tests never touch the developer's real keychain or manifest.

## Starting Context

A test suite is running unit or use-case tests for components that depend on `IConfiguration`.

## Entry Route

Test setup registers test double services into the dependency injection container before calling `AddNyssaConfiguration()`.

## Trigger

An automated test initializes an isolated test container.

## Process

```text
Actor injects test doubles into configuration service
+-- Starting context: test doubles prepared (in-memory keyring, fake clock, etc.)
+-- Entry route: services registration in test setup
+-- Trigger: automated test execution
+-- Step 1: Register test doubles in container
|   +-- services.AddSingleton<IKeyring, InMemoryKeyring>()
|   +-- services.AddSingleton<IBrowserLauncher, FakeBrowserLauncher>()
|   +-- services.AddSingleton<TimeProvider>(fakeTimeProvider)
|   \-- services.AddSingleton<IHttpClientFactory>(mockHttpFactory)
+-- Step 2: Register configuration service
|   \-- services.AddNyssaConfiguration(options => {
|           options.ConfigDirectory = tempTestFolder;
|           options.Bootstrap.ConfigManifest = tempManifestFile;
|       })
+-- Step 3: First service resolution
|   +-- ConfigurationClient resolves IKeyring -> receives InMemoryKeyring
|   +-- Real OS keychain is NEVER contacted
|   \-- User's ~/.nyssa-ai/ files are NEVER modified
\-- Success: Test runs fast, deterministically, and with 100% isolation
```

## Step Details

### Step 1: Register test doubles in container
- **Injectable Seams:**
  - `IKeyring`: Replaces the OS credential store with an in-memory dictionary.
  - `IBrowserLauncher`: Captures OAuth URLs instead of popping open a real desktop browser.
  - `TimeProvider` / Clock: Controls time for token expiration tests without waiting.
  - `IHttpClientFactory`: Intercepts token-endpoint requests.

## Success

Automated tests execute completely offline and in memory without modifying the user's personal workstation credentials or configuration files.

## Failure Cases

- Test assertions fail if behavior deviates from specifications.

## Consistency Check

- Unique coverage: Covers dependency injection test seams and test isolation.
- Closest related use cases: `UC-001` and `UC-002` cover production registrations.
- Contradictions: None found.

## Acceptance

- Tests run without needing an unlocked login session or OS credential manager.
- Running tests leaves the real `~/.nyssa-ai/config-manifest.json` and real OS keychain completely untouched.
