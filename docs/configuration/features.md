# Product Feature Progress

| Delivery Phase | Complete / Total | % Complete |
|----------------|------------------|------------|
| Phase 0 - Foundation | 0 / 3 | 0% |
| Phase 1 - Core Product | 0 / 3 | 0% |
| Phase 2 - Providers, OAuth & Recovery | 0 / 4 | 0% |
| **Overall** | 0 / 10 | 0% |

---

## How to Use This Feature Inventory

This document lists product features in intended build order across the NyssaAI Configuration component (`NyssaAI.Configuration` for .NET and `@nyssa-ai/configuration` for TypeScript/Node.js).

* **Feature IDs:** Canonical features use the `F-Nxx` scheme, where `N` is the delivery phase and `xx` is the two-digit feature number.
* **Feature folders:** Each feature owns a folder named with the lowercase feature ID under `docs/configuration/features/`, for example `docs/configuration/features/f-001/`.
* **Feature files:** The main feature guide is always `feature.md` inside that folder. Use cases live beside it as `uc-001-short-slug.md`.
* **Status:** An item is marked `complete` only when the capability is implemented, documented, and verified across all targeted monorepo configuration packages (`.NET` and `TypeScript/Node.js`).
* **Phases:** Features are grouped into Foundation (Phase 0), Core Product (Phase 1), and External Providers, OAuth & Recovery (Phase 2).

---

## Feature Checklist Summary

* [ ] F-001: Protected File System & Cross-Process Locking
* [ ] F-002: Configuration Manifest & First-Use Provisioning
* [ ] F-003: OS Keyring Custody & Address-Bound Local Encryption
* [ ] F-101: Namespace Document Store & Core Value Operations
* [ ] F-102: Typed DTO Serialization & Namespace Replacement/Merge
* [ ] F-103: Application Registration, Startup Pipeline & Test Seamsyes
* [ ] F-201: Provider Migration (`MoveAsync`)
* [ ] F-202: Interactive OAuth Sign-In & Automatic Token Refresh
* [ ] F-203: Cloud Key Vault Adaptation
* [ ] F-204: Guarded Two-Phase Recovery

## Feature Inventory

### Phase 0 - Foundation

**F-001: Protected File System & Cross-Process Locking**  
Enforces restrictive directory and file permissions (`0700`/`0600` on POSIX, owner/SYSTEM/Administrators DACLs on Windows), verifies private files for secret-bearing manifests, publishes files atomically via temporary files, and coordinates concurrent access through cross-process `.lock` files with exponential backoff.  
[Feature Guide: Protected File System & Cross-Process Locking](features/f-001/feature.md)

**F-002: Configuration Manifest & First-Use Provisioning**  
Reads, validates, and atomically provisions application sections in `~/.nyssa-ai/config-manifest.json` (or a custom manifest path), resolves canonical configuration directories, and applies operator overrides for lock timeouts and cloud key vaults.  
[Feature Guide: Configuration Manifest & First-Use Provisioning](features/f-002/feature.md)

**F-003: OS Keyring Custody & Address-Bound Local Encryption**  
Manages the application's 256-bit local protection key in the platform credential store (`IKeyring` for Windows Credential Manager, macOS Keychain, and Linux Secret Service) without overwriting keys on transient errors, and seals/opens `"aesLocal"` entries using FormatVersion 2 AES-256-GCM bound to `(namespace, key)` AAD.  
[Feature Guide: OS Keyring Custody & Address-Bound Local Encryption](features/f-003/feature.md)

### Phase 1 - Core Product

**F-101: Namespace Document Store & Core Value Operations**  
Persists `<namespace>.json` documents using compact `ConfigurationValue` envelopes and provides `GetAsync`, `SetAsync`, `RemoveAsync`, and ordinal `ListAsync` with provider immutability on update and `ConfigurationRemoval.RemoteValueRemains` retention reporting.  
[Feature Guide: Namespace Document Store & Core Value Operations](features/f-101/feature.md)

**F-102: Typed DTO Serialization & Namespace Replacement/Merge**  
Provides keyed DTO serialization (`GetAsAsync<T>(ns, key)`, `SetAsAsync<T>(ns, key, value)`), whole-namespace property decomposition (`GetAsAsync<T>(ns)`, `SetAsAsync<T>(ns, value)`), shallow partial namespace updates (`MergeSettingsAsync<T>(ns, value)`), typed string encoding preservation, and legacy `namespaceSettings` migration.  
[Feature Guide: Typed DTO Serialization & Namespace Replacement/Merge](features/f-102/feature.md)

**F-103: Application Registration, Startup Pipeline & Test Seams**  
Composes the configuration client (`AddNyssaConfiguration` / `ConfigurationClient`), resolves default application identity, runs the ordered startup initialization pipeline, and exposes dependency-injection seams for `IKeyring`, `IBrowserLauncher`, clock/time providers, HTTP clients, and vault factories.  
[Feature Guide: Application Registration, Startup Pipeline & Test Seams](features/f-103/feature.md)

### Phase 2 - Providers, OAuth & Recovery

**F-201: Provider Migration (`MoveAsync`)**  
Migrates an existing configuration entry across plaintext, `"aesLocal"`, and cloud vault providers by writing to the destination provider before erasing from the source provider and reporting `ConfigurationMove.SourceWithdrawn`.  
[Feature Guide: Provider Migration (`MoveAsync`)](features/f-201/feature.md)

**F-202: Interactive OAuth Sign-In & Automatic Token Refresh**  
Implements `IConfigurationExtensions` (`AuthorizeAsync`, `AuthorizeProviderAsync`, `ReauthorizeAsync`) using HTTPS PKCE loopback flows, stores self-describing encrypted `OAuthToken` credentials under `"oauth"`, refreshes tokens under namespace locks within a 5-minute expiry skew, retains rotated grants in memory if local publication fails, and marks revoked grants with `reauth_required`.  
[Feature Guide: Interactive OAuth Sign-In & Automatic Token Refresh](features/f-202/feature.md)

**F-203: Cloud Key Vault Adaptation**  
Adapts external secret vaults (`ICloudKeyVaultFactory` and `ICloudKeyVault`) into configuration providers storing local `ConfigurationPointer` entries, supplies `IAccessTokenSource` for OAuth-backed vaults, supports static manifest secrets, and blocks circular credential dependencies (`CircularCredential`).  
[Feature Guide: Cloud Key Vault Adaptation](features/f-203/feature.md)

**F-204: Guarded Two-Phase Recovery**  
Captures deterministic per-step progress (`ConfigurationOperationProgress`) when `MoveAsync`, namespace `SetAsAsync`, or `MergeSettingsAsync` fails mid-flight, issues HKDF-encrypted/authenticated `ConfigurationRecoveryHandle` tokens (`Export`/`Import`), and reconciles/completes unfinished work via `RecoverAsync` and `ICloudKeyVaultRecovery`.  
[Feature Guide: Guarded Two-Phase Recovery](features/f-204/feature.md)
