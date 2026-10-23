# Product

## Purpose

NyssaAI Configuration (`NyssaAI.Configuration` for .NET and `@nyssa-ai/configuration` for TypeScript/Node.js) is a cross-platform configuration and secret-management library for applications and command-line tools. It provides a single consistent interface (`IConfiguration` and `IConfigurationExtensions`) to read, write, list, remove, migrate, and recover settings that persist across runs — whether stored as plain JSON, encrypted locally using an OS-keyring-protected key (`aesLocal`), managed as self-refreshing OAuth grants (`oauth`), or held in a remote cloud secret vault.

## Audience

- **Application and CLI Engineers:** Developers building .NET, Node.js/TypeScript, and future polyglot services or CLI tools in the NyssaAI ecosystem who need reliable local and cloud-backed configuration without writing custom file locking, encryption, or token-refresh plumbing.
- **Vault Integration Authors:** Developers implementing pluggable secret store adapters (`ICloudKeyVaultFactory`, `ICloudKeyVault`, and `ICloudKeyVaultRecovery`) for external vaults such as Bitwarden Secrets Manager, Azure Key Vault, or HashiCorp Vault.
- **System Operators & DevOps:** Operators who provision or override application storage paths, lock timeouts, and key vault bindings through `~/.nyssa-ai/config-manifest.json` without recompiling application code.

## Problem

Applications and CLI tools routinely manage four distinct categories of state: plain settings, locally encrypted secrets, interactive OAuth credentials, and cloud vault secrets. Without a unified component, each application repeatedly implements:

- Ad-hoc JSON file persistence that is vulnerable to torn writes, race conditions across concurrent processes, or overly permissive OS file ACLs.
- Custom key custody logic that risks leaking encryption keys to disk or silently regenerating keys during transient OS keyring outages (orphaning existing encrypted data).
- Disparate APIs for reading plain settings versus secret vault entries or refreshing expiring OAuth access tokens.
- Fragile multi-property writes and provider migrations that leave remote vaults and local configuration files in an inconsistent state when interrupted.

## Solution

NyssaAI Configuration unifies configuration and secret access behind `IConfiguration` and `IConfigurationExtensions`, backed by a manifest-driven `ConfigurationStore`:

- **Zero-ceremony local defaults:** On first resolution, the library locates or provisions the application's section in `~/.nyssa-ai/config-manifest.json`, verifies and prepares a restricted configuration directory, and retrieves or generates a 256-bit key in the OS credential store.
- **Namespace-scoped document store:** Settings are organized by namespace (`<namespace>.json`), supporting individual key operations (`GetAsync`, `SetAsync`, `RemoveAsync`, `ListAsync`), single-key DTOs, and multi-property namespace DTOs (`SetAsAsync`, `GetAsAsync`, `MergeSettingsAsync`) where each property can independently use plaintext, `"aesLocal"`, `"oauth"`, or a named cloud vault.
- **Address-bound encryption and strict key custody:** Local secrets use AES-256-GCM (FormatVersion 2) with length-prefixed `(namespace, key)` Additional Authenticated Data (AAD) so ciphertext cannot be transplanted across keys or namespaces.
- **Interactive OAuth & cloud vault seams:** Interactive CLI commands authorize OAuth grants via PKCE and a loopback listener (`IConfigurationExtensions`), while ordinary reads transparently refresh tokens within a 5-minute expiry window. Pluggable vault adapters store remote values and persist compact `ConfigurationPointer` references locally.
- **Guarded two-phase recovery:** Provider moves and namespace writes track per-step progress (`ConfigurationOperationProgress`) and emit an HKDF-authenticated `ConfigurationRecoveryHandle` so callers can safely resume interrupted operations via `RecoverAsync`.

## Boundaries

- **External vault SDKs are separate packages:** Concrete cloud vault implementations (e.g., Bitwarden, Azure Key Vault, AWS Secrets Manager) are never bundled inside the core configuration package; only the adapter contracts and orchestration live here.
- **No environment-variable configuration injection:** Nothing in the manifest reader or bootstrap pipeline reads process environment variables to select paths or providers, preventing untrusted environment manipulation in AI or automation harnesses.
- **No background retry daemons or disk journals:** The library does not run background recovery threads or write recovery metadata into `<namespace>.json`; continuation of interrupted operations is explicitly triggered by the caller using `ConfigurationRecoveryHandle`.
- **Unix ownership is not checked:** On POSIX platforms, the library enforces `0700` directory and `0600` file permission bits and rejects group/other write (or read for secret-bearing manifests), but does not inspect `uid`/`gid` ownership.

## Success

- **Wire-compatible polyglot storage:** Both `NyssaAI.Configuration` (.NET) and `@nyssa-ai/configuration` (TypeScript/Node.js) read and write identical `config-manifest.json` files, `<namespace>.json` envelopes, `.lock` files, and FormatVersion 2 `aesLocal` ciphertexts.
- **Fail-closed security posture:** Startup and file operations reject unsafe directory permissions, secret-bearing manifests readable by other users, and unavailable OS credential stores before exposing or overwriting sensitive data.
- **Single read/write mental model:** Consumer classes read settings and secrets through `IConfiguration` without knowing whether an entry is plaintext, locally encrypted, an OAuth bearer token, or a cloud vault pointer.
- **Deterministic concurrency and recovery:** Concurrent processes coordinate safely through cross-process lock files and atomic temp-file replacement, and interrupted multi-step operations surface structured diagnostics (`ConfigurationOperationProgress`) with safe recovery handles.

## Features

The feature inventory, build order, and detailed feature guides are maintained in [docs/configuration/features.md](features.md) and `docs/configuration/features/`.

## Tech Stack

Runtime, framework, storage, tooling, and environment assumptions are maintained in [docs/configuration/tech-stack.md](tech-stack.md).
