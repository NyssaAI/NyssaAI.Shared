# Product Roadmap

This document lists work slices in delivery order. [docs/configuration/features.md](features.md) owns the feature inventory, phases, and status; this roadmap owns dependency-driven delivery order and slicing across `NyssaAI.Configuration` (.NET) and `@nyssa-ai/configuration` (TypeScript/Node.js).

---

## How to Use This Roadmap

* **Slice IDs:** Each slice uses the `R-NNN` scheme. IDs are assigned once and never renumbered or reused; the next ID is the highest ever assigned plus one.
* **Order:** The list order below is the delivery order. Inserting a slice mid-sequence takes the next unused number and the right position — existing IDs never change.
* **Statement:** Each slice is one human-readable statement line: `- [ ] R-NNN: Imperative statement — [F-Nxx](features/f-001/feature.md)`, with `— after: ...` appended only when the slice declares dependencies.
* **Detail bullets:** Indented sub-bullets under the statement describe the work in plain language. Keep detailed requirements in the feature guide; the roadmap stays scannable.
* **Verify checkpoint:** The `Verify:` sub-bullet is a user-verifiable stopping point: `Verify:` followed by what the user runs or clicks, what they should see when the slice is correct, and the demo data to use when observing it needs data. A slice with no user-observable outcome of its own may defer with `Verify: with R-NNN`, naming a later slice whose checkpoint demonstrates it — but checkpoints must occur regularly: never more than two consecutive open slices defer. Keep detailed acceptance examples and demo data in the feature guide; the roadmap line stays one scannable instruction.
* **Parent feature:** Each slice references exactly one feature and links to its guide, for example `[F-001](features/f-001/feature.md)`. A feature may be covered by more than one slice.
* **Dependencies:** An optional `after:` list names the slice or feature IDs this slice must follow. A feature ID means "after all existing slices of that feature." A slice must not name its own parent feature.
* **Status:** Check a slice only when its work is implemented and verified. Completed slices are immutable history — rework on a shipped feature is a new slice, never an unchecked old one.
* **Coverage:** Every feature not yet complete in [docs/configuration/features.md](features.md) needs at least one slice here. Completed features and non-feature capability entries are exempt.

---

## Slices

- [ ] R-001: Align protected file system checks, private-file verification, and cross-process locking across .NET and Node.js — [F-001](features/f-001/feature.md)
    - Verify POSIX `0700`/`0600` permission enforcement, Windows DACL ownership and inheritance checks (`DirectorySafety`), and private-file read checks (`VerifyPrivateFile`) for secret-bearing manifests in both `NyssaAI.Configuration` and `@nyssa-ai/configuration`.
    - Ensure atomic temporary-file publication and cross-process `.lock` file acquisition with exponential backoff behave identically across runtimes.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~FileSecurity` and `npm --prefix nyssa-ai-configuration test -- test/unit/ProtectedFileSystem.test.ts` and confirm all permission, private-file, and lock contention tests pass with 0 failures.
- [ ] R-002: Enforce full manifest v1 parity including relative path resolution, private-file secret checks, and bootstrap overrides — [F-002](features/f-002/feature.md) — after: R-001
    - Match `ConfigurationManifestReader` and `ConfigurationManifestWriter` behavior across .NET and Node.js for relative `configurationDirectory` resolution against the manifest directory, sidecar-locked atomic section provisioning, and `lockTimeout` / `keyVault` override layering.
    - Enforce `VerifyPrivateFile` when `keyVault.secrets` is populated and distinguish missing manifest/section (`NotFound`) from permission or parse failures.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~Manifest` and `npm --prefix nyssa-ai-configuration test -- test/unit/ConfigurationManifest.test.ts` and confirm manifest provisioning, relative paths, and secret permission checks pass.
- [ ] R-003: Verify OS keyring three-way status custody and FormatVersion 2 AES-256-GCM cross-language wire compatibility — [F-003](features/f-003/feature.md) — after: R-001
    - Implement and verify platform `IKeyring` adapters and `KeyringManagedSecret` custody at `(<ApplicationName>, "configuration-key")` so transient keyring errors never overwrite an existing key and key buffers are zeroed after cipher setup.
    - Verify `ConfigurationCipherAesGcm` FormatVersion 2 (`0x02` + 12-byte nonce + 16-byte tag + ciphertext in Base64URL with big-endian length-prefixed `(namespace, key)` AAD) across .NET and Node.js test vectors.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~Encryption` and `npm --prefix nyssa-ai-configuration test -- test/unit/ConfigurationCipherAesGcm.test.ts` and confirm shared FormatVersion 2 ciphertext vectors decrypt identically in both runtimes.
- [ ] R-004: Complete core namespace store operations, compact JSON envelopes, and `ConfigurationRemoval` retention reporting — [F-101](features/f-101/feature.md) — after: R-002, R-003
    - Ensure `GetAsync`, `SetAsync`, `RemoveAsync`, and ordinal `ListAsync` in `@nyssa-ai/configuration` match `NyssaAI.Configuration`, including compact `ConfigurationValue` serialization (`value`, omitting default `provider`, `encrypted`, `reauth_required`, `json`), provider immutability on `SetAsync`, and `ConfigurationRemoval.RemoteValueRemains` return values.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~ConfigurationTutorials` and `npm --prefix nyssa-ai-configuration test -- test/unit/ConfigurationStore.test.ts` and confirm key CRUD, provider immutability, ordinal listing, and `RemoteValueRemains` outcomes pass.
- [ ] R-005: Implement whole-namespace DTO replacement, `MergeSettingsAsync`, typed string metadata, and legacy `namespaceSettings` migration — [F-102](features/f-102/feature.md) — after: R-004
    - Add namespace-scoped `GetAsAsync<T>(namespace)`, `SetAsAsync<T>(namespace, settings)`, and `MergeSettingsAsync<T>(namespace, settings)` to `@nyssa-ai/configuration` alongside keyed DTO overloads.
    - Preserve per-property providers on namespace replace and shallow merge, maintain `"json": true` metadata for typed string reads (`GetAsAsync<string>`), and support legacy `namespaceSettings` expansion and conflict checks.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~Namespace` and `npm --prefix nyssa-ai-configuration test -- test/useCase/NamespaceWriteTutorials.test.ts` with a multi-property `MailSettings` DTO (`Host`, `Port`, nested `Retry`) and confirm replace, shallow merge, and typed string reads match across both runtimes.
- [ ] R-006: Align client startup pipeline, option defaults, and test substitution seams — [F-103](features/f-103/feature.md) — after: R-005
    - Align `ConfigurationClient` and startup options (`ApplicationName`, `ConfigDirectory`, `Bootstrap.ConfigManifest`, `Bootstrap.LockTimeout`, `Bootstrap.KeyVault`) and substitution seams (`IKeyring`, `IBrowserLauncher`, clock/time provider, HTTP client, `ICloudKeyVaultFactory`, logger) between .NET and Node.js.
    - Ensure startup eagerly validates the manifest, prepares the configuration directory, and ensures the encryption key exists in `IKeyring` even when only plaintext values are written.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~SetupTutorials` and `npm --prefix nyssa-ai-configuration test -- test/useCase/SetupTutorials.test.ts` and confirm startup order, failure modes, and isolated test seams pass without modifying `~/.nyssa-ai/config-manifest.json`.
- [ ] R-007: Deliver provider migration (`MoveAsync`) with destination-first write and `SourceWithdrawn` reporting — [F-201](features/f-201/feature.md) — after: R-005
    - Implement `MoveAsync(namespace, key, toProvider?)` in `@nyssa-ai/configuration` matching `NyssaAI.Configuration`: write resolved value to destination provider first, update local entry, erase from source provider, and return `ConfigurationMove` with `SourceWithdrawn`.
    - Reject moving a missing key or moving an entry to its existing provider, and treat omitted/`null` `toProvider` as returning to default plaintext storage.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~Move` and `npm --prefix nyssa-ai-configuration test -- test/useCase/ConfigurationMoveTutorials.test.ts` moving `"server"/"port"` from plaintext to `"aesLocal"` and back to plaintext, confirming `SourceWithdrawn === true` and on-disk JSON transitions.
- [ ] R-008: Implement interactive PKCE OAuth authorization (`IConfigurationExtensions`) and locked token refresh (`OAuthTokenProvider`) — [F-202](features/f-202/feature.md) — after: R-006
    - Implement `AuthorizeAsync`, `AuthorizeProviderAsync`, and `ReauthorizeAsync` with HTTPS endpoint validation, PKCE S256 challenge generation, `127.0.0.1` loopback callback listener, `IBrowserLauncher`, and self-describing encrypted `OAuthToken` storage under provider `"oauth"`.
    - Implement `OAuthTokenProvider` read fast-path, double-checked namespace-locked token refresh within the 5-minute expiry skew, in-memory rotated grant retention on local write failure, and `reauth_required: true` marking on revoked grants (`Forbidden`).
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~OAuthTutorials` and `npm --prefix nyssa-ai-configuration test -- test/useCase/OAuthTutorials.test.ts` against a fake authorization server and confirm PKCE sign-in, concurrent expiry refresh, rotated grant recovery, and revoked grant reauthorization pass.
- [ ] R-009: Implement pluggable Cloud Key Vault adaptation (`ICloudKeyVaultFactory`, `ICloudKeyVault`, `IAccessTokenSource`, and `CircularCredential` guards) — [F-203](features/f-203/feature.md) — after: R-007, R-008
    - Implement `ICloudKeyVaultFactory`, `ICloudKeyVault`, `ConfigurationPointer`, and `IAccessTokenSource` in `@nyssa-ai/configuration` matching `NyssaAI.Configuration`.
    - Enforce `CircularCredential` guards preventing a vault's OAuth credential entry from being stored in or read through that same vault, and support multi-provider DTO assembly combining plaintext, `"aesLocal"`, and cloud vault pointers.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~NamespaceProvidersTutorials` and `npm --prefix nyssa-ai-configuration test -- test/useCase/NamespaceProvidersTutorials.test.ts` assembling a `MailserverSettings` DTO from plaintext, `"aesLocal"`, and a fake cloud vault.
- [ ] R-010: Implement guarded two-phase recovery (`ICloudKeyVaultRecovery`, `ConfigurationOperationProgress`, `ConfigurationRecoveryHandle`, and `RecoverAsync`) — [F-204](features/f-204/feature.md) — after: R-007, R-009
    - Implement `ICloudKeyVaultRecovery` (`StoreIdentity`, `PrepareAsync`, `ExecuteAsync`, `ReconcileAsync`), `ConfigurationOperationProgress`, `ConfigurationOperationException`, `ConfigurationOperationCanceledException`, HKDF-SHA256 encrypted/authenticated `ConfigurationRecoveryHandle` (`Export`/`Import` with redacted `ToString`), and `RecoverAsync(handle)` in `@nyssa-ai/configuration` to match `NyssaAI.Configuration`.
    - Ensure mid-flight failures during `MoveAsync`, namespace `SetAsAsync`, and `MergeSettingsAsync` emit payload-free progress receipts and resume cleanly via `RecoverAsync` without re-serializing DTOs or deleting destination entries after a published move.
    - Verify: run `dotnet test NyssaAI.Shared.slnx --filter FullyQualifiedName~ConfigurationRecoveryTutorials` and `npm --prefix nyssa-ai-configuration test -- test/useCase/ConfigurationRecoveryTutorials.test.ts` simulating interrupted vault writes and moves, exporting/importing the recovery handle, and confirming `RecoverAsync` reaches `Completed`.
