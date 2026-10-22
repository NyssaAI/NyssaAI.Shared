---
title: "feat: Add @nyssa-ai/configuration Node.js component"
date: 2026-10-22
type: feat
artifact_contract: ce-unified-plan/v1
product_contract_source: ce-plan-bootstrap
execution: code
---

## Goal Capsule

- **Objective:** Provide a first-class, idiomatic TypeScript/Node.js implementation of `NyssaAI.Configuration` (`@nyssa-ai/configuration`) in this monorepo that shares identical on-disk stores, schema formats, cross-process locking, file security, and AES-256-GCM encrypted values with the existing .NET component.
- **Means:** Build an independent package under `packages/configuration` adhering strictly to Jeremy Maynor's TypeScript `dev-standards` (Single Responsibility ASCII trees, discriminated-union `Result`, structured logging boundary, strict typing, and Vitest public use-case tests) with zero external runtime crypto dependencies (built-in `node:crypto`).
- **Authority Hierarchy:** Product security invariants (POSIX/Windows permissions, non-leakage of secrets, authenticated cipher) > Cross-language wire & locking compatibility > Local TypeScript standards > Convenience.
- **Stop Conditions:** Complete when all implementation units are verified with fast unit tests and public use-case tests demonstrating parity with .NET configuration files.

---

## Product Contract

### Summary
The `@nyssa-ai/configuration` component provides secure, persistent configuration and secret management for Node.js backend services and CLI tools. It reads and writes plain and encrypted settings while storing them in cross-process locked JSON files. It avoids storing secrets in environment variables, protecting applications from supply-chain exfiltration and process-tree inspection.

### Problem Frame
Node.js applications conventionally rely on `process.env` and `.env` files for configuration and secrets. In multi-tenant, microservice, or CLI environments, this presents severe security vulnerabilities: any compromised third-party npm package can inspect `process.env` and exfiltrate secrets, and child processes or diagnostic crash reporters routinely leak environment tables. Furthermore, teams working across .NET and Node.js require a single unified workstation configuration layout without duplicate configuration stores.

### Requirements

- R1. **Manifest Resolution:** The component must resolve the user manifest at `~/.nyssa-ai/config-manifest.json` by default (with custom manifest path support) using JSON Schema version 1, reading and provisioning application sections under `applications.<AppName>`. Must parse .NET `TimeSpan` strings for `lockTimeout` (e.g. `"00:00:10"`) via a named parser per TS-DATA.
- R2. **Configuration Directory Resolution:** The component must read `configurationDirectory` from the application's manifest section as canonical. If the section does not exist on first run, it must provision the section using `<app-base>/.nyssa-ai/config` (or a code-specified directory) and record it into the manifest.
- R3. **Namespace JSON Storage:** Configuration values must be stored in `<configDir>/<namespace>.json`, containing a map of property keys to polymorphic `ConfigurationValue` entries:
  - Literal unencrypted values store raw JSON data (`value: string | number | boolean | object`).
  - Provider or encrypted entries store `value` as string with `provider: string` and `encrypted?: boolean`.
  - Flags `json?: boolean` and `reauth_required?: boolean` are preserved and omitted when false.
- R4. **AES-256-GCM Encryption (`aesLocal`):** Locally encrypted secrets must match format version 2:
  - 1-byte version (`0x02`) + 12-byte random nonce + 16-byte authentication tag + ciphertext.
  - Associated Authenticated Data (AAD): `[0x02] + [Int32BE(namespaceLen)] + [namespaceBytes] + [Int32BE(keyLen)] + [keyBytes]`.
  - Exact ordinal case-sensitivity preserved for namespace and key names.
  - Base64URL-encoded string output without padding.
- R5. **OS Keyring Integration & Explicit Container Fallback:** The 32-byte master key must be managed via the OS credential store under service `<AppName>` and entry `configuration-key`. To avoid silent key divergence and permanent secret orphaning, fallback to `<configDir>/.nyssa-key` (mode `0600`) is only permitted when explicitly configured via options or when the native credential store is explicitly marked absent on system configuration.
- R6. **Cross-Process File Locking:** Namespace reads and writes must acquire a cross-process lock on `<namespace>.json.lock` matching .NET's handle-based mutual exclusion (accommodating existing non-deleted lock files and supporting kernel advisory locks). Contention retries with exponential backoff starting at 5ms up to the configured `lockTimeout` (default 10s).
- R7. **Atomic Writes with Collision-Free Temps:** Namespace file writes must write to unique sibling temp files (`<namespace>.json.<uuid>.tmp`) with strict permissions and rename atomically to destination.
- R8. **OS File Security (POSIX & Windows):** Newly created directories must enforce mode `0700` on POSIX. On Windows, inheritance from parent folders must be stripped and ACLs restricted to Current User, SYSTEM, and Administrators to satisfy .NET's `DirectorySafety.Verify`. Files must enforce mode `0600` on POSIX.
- R9. **TypeScript Dev Standards Conformance:**
  - One concept per file with Single Responsibility comment and ASCII interface tree (TS-FILE, TS-HEADER).
  - Feature-first layout with no forbidden catch-all folders (`contracts/`, `helpers/`, `utils/`) (TS-STRUCTURE).
  - Expected operational failures return discriminated-union `Result<T>` (TS-FAILURE).
  - All asynchronous I/O methods accept and honor `signal?: AbortSignal` (TS-CONSTRUCTION).
  - Boundary logging with monotonic duration, correlation, and contention degradation warnings (TS-LOG).
  - Vitest tests following strict `subject_scenario_expected` naming (TS-TEST).

### Scope Boundaries

#### In Scope
- Monorepo package scaffold under `packages/configuration/` (TypeScript 5, Node.js LTS, ESM).
- Manifest parser, reader, and writer matching .NET `config-manifest.json`.
- Namespace file reader/writer with atomic rename and `.lock` cross-process locking.
- Built-in AES-256-GCM cipher with big-endian length-prefixed AAD.
- Keyring adapter with OS integration and file-fallback for headless containers.
- Core public API: `IConfiguration` with `get`, `getAs`, `set`, `setAs`, `remove`.
- Comprehensive Vitest unit and use-case test suite.

#### Considered and Not Built
- **Cloud Key Vault Providers (Azure Key Vault, Bitwarden Secrets, HashiCorp Vault):** Deferred to separate provider packages (e.g. `@nyssa-ai/vault-bitwarden`), exactly mirroring the .NET architecture where `NyssaAI.Configuration` core stays minimal and Native AOT/zero-dependency friendly.
- **Global `process.env` mutator:** Explicitly rejected; the library's primary mission is eliminating secrets from the process environment block.

---

## Planning Contract

### Key Technical Decisions

- **KTD1. Monorepo Location & Package Layout:**
  *(session-settled: user-directed — chosen over separate repo)*
  Place the package in `packages/configuration/` at the repository root. This isolates the Node/npm workspace from the root .NET solution while allowing shared tooling and documentation.

- **KTD2. Zero-Dependency Cryptography:**
  Use Node.js built-in `node:crypto` (`createCipheriv('aes-256-gcm', key, iv)`, `createDecipheriv`, `randomBytes`) rather than external WebCrypto or sodium wrappers. Ensures high performance, zero supply-chain risk, and exact byte-level control over nonce, auth tag, and AAD packing.

- **KTD3. Cross-Process Locking Mechanism:**
  Avoid naive `O_EXCL` file creation which causes permanent deadlocks against existing .NET lock files (which remain on disk). Implement file-handle based locking on `<namespace>.json.lock` using kernel advisory locking (`flock` on POSIX via native/helper bindings) and mutual exclusion locks, acquiring the lock on **both** reads and writes to avoid Windows atomic rename lockout (`EPERM`/`EBUSY`). Implement exponential backoff starting at 5ms up to `lockTimeout` (default 10s), honoring `AbortSignal`.

- **KTD4. Keyring Strategy & Non-Divergence Guarantee:**
  To prevent silent key divergence and secret loss, native OS keyring (Linux Secret Service / D-Bus, Windows Credential Manager, macOS Keychain) is attempted first. A momentary store error throws/fails rather than generating a new key. The fallback to `<configDir>/.nyssa-key` (mode `0600`) is an explicit opt-in setting for headless servers and containerized CI environments, ensuring Node and .NET behavior remains strictly aligned.

- **KTD5. Result Type Architecture & TS-STRUCTURE:**
  Standardize on Jeremy Maynor's discriminated-union `Result<T>` in `src/results/Result.ts` (eliminating the forbidden `src/contracts/` catch-all folder per TS-STRUCTURE):
  ```typescript
  export type Result<T> =
    | { readonly ok: true; readonly value: T }
    | { readonly ok: false; readonly error: string };
  ```
  All async I/O accepts and honors `signal?: AbortSignal` (TS-CONSTRUCTION). Abortions reject rather than returning a cancellation `Result` (TS-FAILURE).

### High-Level Technical Design

```
+-------------------------------------------------------------------------+
|                              Consumer App                               |
+-------------------------------------------------------------------------+
                                    |
                                    v
                     +-------------------------------+
                     |        IConfiguration         |
                     +-------------------------------+
                                    |
                     +-------------------------------+
                     |      ConfigurationClient      |
                     +-------------------------------+
                        /           |           \
                       v            v            v
        +------------------+ +-------------+ +--------------------+
        |  ManifestReader  | | CipherAesGcm| | DocumentLocalFile  |
        |  ~/.nyssa-ai/    | | AES-256-GCM | | <dir>/<ns>.json    |
        |  config-manifest | | (Format v2) | | <dir>/<ns>.json.lock|
        +------------------+ +-------------+ +--------------------+
                                    |                   |
                                    v                   v
                             +-------------+     +----------------+
                             |  IKeyring   |     | FileSecurity   |
                             | (SecretServ/|     | (0700 dirs,    |
                             |  Protected) |     |  0600 files)   |
                             +-------------+     +----------------+
```

### Output Structure

```
packages/configuration/
|-- package.json
|-- tsconfig.json
|-- vitest.config.ts
|-- README.md
|-- src/
|   |-- index.ts
|   |-- IConfiguration.ts
|   |-- ConfigurationOptions.ts
|   |-- results/
|   |   `-- Result.ts
|   |-- encryption/
|   |   |-- ConfigurationCipherAesGcm.ts
|   |   `-- IConfigurationCipher.ts
|   |-- fileSecurity/
|   |   |-- DirectorySafety.ts
|   |   `-- ProtectedFileSystem.ts
|   |-- keyring/
|   |   |-- IKeyring.ts
|   |   |-- KeyringProtectedFile.ts
|   |   `-- KeyringDefault.ts
|   |-- logging/
|   |   |-- IConfigurationLogger.ts
|   |   `-- NullConfigurationLogger.ts
|   |-- manifest/
|   |   |-- ConfigurationManifest.ts
|   |   |-- ConfigurationManifestReader.ts
|   |   `-- ConfigurationManifestWriter.ts
|   `-- values/
|       |-- ConfigurationDocumentLocalFile.ts
|       |-- ConfigurationValue.ts
|       `-- ConfigurationStore.ts
`-- test/
    |-- unit/
    |   |-- CipherAesGcm.test.ts
    |   |-- ManifestReader.test.ts
    |   |-- ProtectedFileSystem.test.ts
    |   `-- DocumentLock.test.ts
    `-- useCase/
        |-- ConfigurationClient.usecase.test.ts
        `-- CrossPlatformParity.usecase.test.ts
```

---

## Implementation Units

### U1. Package Scaffolding & Core Result Contract
- **Goal:** Initialize `packages/configuration` workspace with strict TypeScript configuration, Vitest runner, package metadata, and the baseline `Result<T>` discriminated-union contract in `src/results/Result.ts` (avoiding forbidden catch-all folders per TS-STRUCTURE).
- **Requirements:** R9
- **Dependencies:** None
- **Files:**
  - `packages/configuration/package.json`
  - `packages/configuration/tsconfig.json`
  - `packages/configuration/vitest.config.ts`
  - `packages/configuration/src/results/Result.ts`
  - `packages/configuration/test/unit/Result.test.ts`
- **Approach:**
  1. Setup package targeting Node.js 20+ LTS, ESM modules, strict compiler flags (`noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `verbatimModuleSyntax`).
  2. Implement `Result.ts` with Single Responsibility header, ASCII interface tree, and immutable `ok`/`fail` constructor functions.
- **Patterns to follow:** `references/typescript/examples/results/Result.ts`
- **Test scenarios:**
  - `result_success_containsValue`: `ok(123)` returns `{ ok: true, value: 123 }`.
  - `result_failure_containsError`: `fail("ERR")` returns `{ ok: false, error: "ERR" }`.
- **Verification:** `pnpm test` or `npm test` runs in `packages/configuration` and passes.

### U2. Cross-Platform Security & File System Protection (POSIX & Windows)
- **Goal:** Provide directory and file creation helpers that strictly enforce Unix permissions (`0700` for directories, `0600` for files) and Windows ACL inheritance removal to satisfy .NET's `DirectorySafety.Verify`.
- **Requirements:** R8, R9
- **Dependencies:** U1
- **Files:**
  - `packages/configuration/src/fileSecurity/ProtectedFileSystem.ts`
  - `packages/configuration/src/fileSecurity/DirectorySafety.ts`
  - `packages/configuration/test/unit/ProtectedFileSystem.test.ts`
- **Approach:**
  1. Implement `ProtectedFileSystem.createDirectory`: on POSIX, sets mode `0o700`; on Windows, strips inherited permissions via `icacls.exe` / native security calls and restricts full control to Current User, SYSTEM, and Administrators (mirroring .NET's `ProtectedFileSystem.CreateWindows`).
  2. Implement `ProtectedFileSystem.createFile` writing atomic files with mode `0o600`.
  3. Implement `DirectorySafety.verify` validating that neither world/group on POSIX nor Authenticated Users/Everyone on Windows have write/modify access.
- **Patterns to follow:** `NyssaAI.Configuration/src/FileSecurity/ProtectedFileSystem.cs` and `DirectorySafety.cs`
- **Test scenarios:**
  - `protectedFileSystem_newDirectoryOnPosix_sets0700Mode`: Verifies directory mode on POSIX is `0o700`.
  - `protectedFileSystem_newFileOnPosix_sets0600Mode`: Verifies file mode on POSIX is `0o600`.
  - `directorySafety_unsafePermissions_returnsFailure`: Simulates insecure directory and asserts validation failure `Result`.
- **Verification:** Security tests pass on both Windows and POSIX.

### U3. AES-256-GCM Cipher Implementation (`aesLocal` Parity)
- **Goal:** Implement byte-for-byte compatible AES-256-GCM sealing and opening matching `ConfigurationCipherAesGcm.cs`.
- **Requirements:** R4, R9
- **Dependencies:** U1
- **Files:**
  - `packages/configuration/src/encryption/IConfigurationCipher.ts`
  - `packages/configuration/src/encryption/ConfigurationCipherAesGcm.ts`
  - `packages/configuration/test/unit/CipherAesGcm.test.ts`
- **Approach:**
  1. Build length-prefixed big-endian AAD packer: `[0x02, int32BE(nsLen), nsBytes, int32BE(kLen), kBytes]`. Ensure exact ordinal case preservation.
  2. Implement `seal`: Generate 12-byte random nonce, compute 16-byte auth tag, concatenate `[0x02, nonce(12), tag(16), ciphertext]`, encode as Base64URL without padding.
  3. Implement `open`: Parse header, verify version `0x02`, decrypt with AAD; return `Result.failure` on tampering, wrong namespace/key, or malformed payload.
- **Patterns to follow:** `NyssaAI.Configuration/src/Encryption/ConfigurationCipherAesGcm.cs`
- **Test scenarios:**
  - `cipherAesGcm_validPayload_roundtripsCorrectly`: Encrypts plaintext and opens it to match original.
  - `cipherAesGcm_differentNamespace_failsAuthentication`: Changing namespace produces authentication failure `Result`.
  - `cipherAesGcm_tamperedCiphertext_failsAuthentication`: Flipping a byte in ciphertext causes auth failure.
  - `cipherAesGcm_dotNetVector_opensCorrectly`: Feeds a known ciphertext vector produced by C# cipher and asserts exact plaintext recovery.
- **Verification:** Crypto unit tests pass including cross-runtime interoperability vectors.

### U4. Manifest Reader, Writer & TimeSpan Parsing
- **Goal:** Implement reading, writing, and provisioning of `~/.nyssa-ai/config-manifest.json` with named TimeSpan parsing.
- **Requirements:** R1, R2, R9
- **Dependencies:** U1, U2
- **Files:**
  - `packages/configuration/src/manifest/ConfigurationManifest.ts`
  - `packages/configuration/src/manifest/ConfigurationManifestReader.ts`
  - `packages/configuration/src/manifest/ConfigurationManifestWriter.ts`
  - `packages/configuration/src/manifest/TimeSpanParser.ts`
  - `packages/configuration/test/unit/ManifestReader.test.ts`
- **Approach:**
  1. Implement `TimeSpanParser.parse` to convert .NET `hh:mm:ss[.fff]` strings to milliseconds per TS-DATA.
  2. Resolve default manifest path using `os.homedir()` + `.nyssa-ai/config-manifest.json`.
  3. Read application section; resolve relative `configurationDirectory` against the manifest's parent directory, and absolute paths directly.
  4. Write/provision application sections safely using atomic file replacement.
- **Patterns to follow:** `NyssaAI.Configuration/src/Manifest/ConfigurationManifestReader.cs`
- **Test scenarios:**
  - `manifestReader_existingSection_resolvesAbsoluteDir`: Correctly parses existing manifest JSON.
  - `manifestReader_relativeDir_resolvesRelativeToManifest`: Correctly resolves relative path against manifest location.
  - `timeSpanParser_dotNetFormat_parsesToMilliseconds`: Validates `"00:00:10"` converts to 10000ms.
  - `manifestWriter_newApplication_provisionsSection`: Adds application section without modifying existing entries.
- **Verification:** Unit tests pass verifying manifest serialization, relative resolution, and TimeSpan parsing.

### U5. Keyring Integration & Key Custody Policy
- **Goal:** Provide master encryption key generation and storage via native OS credential stores, with explicit container fallback to avoid silent key divergence.
- **Requirements:** R5, R9
- **Dependencies:** U1, U2
- **Files:**
  - `packages/configuration/src/keyring/IKeyring.ts`
  - `packages/configuration/src/keyring/KeyringProtectedFile.ts`
  - `packages/configuration/src/keyring/KeyringDefault.ts`
  - `packages/configuration/test/unit/Keyring.test.ts`
- **Approach:**
  1. Define `IKeyring` contract: `get(service, account, signal?)` and `set(service, account, secret, signal?)`.
  2. Store key material encoded as Base64 strings to match .NET's `KeyringManagedSecret`.
  3. Implement strict custody policy: transient store failure throws/fails instead of generating a new key (preventing silent secret loss).
  4. Implement `KeyringProtectedFile` in `<configDir>/.nyssa-key` (mode `0600`) as an explicit opt-in for headless/CI environments.
  5. Wipe in-memory key buffers (`buf.fill(0)`) immediately after cipher instantiation.
- **Patterns to follow:** `NyssaAI.Configuration/src/OsKeyring/KeyringManagedSecret.cs`
- **Test scenarios:**
  - `keyring_getOrCreate_generatesAndRetrievesConsistentKey`: First read generates key; second read retrieves identical key.
  - `keyringProtectedFile_createNew_sets0600Mode`: Verifies key file on disk has strict permissions.
  - `keyring_transientFailure_failsWithoutOverwritingKey`: Transient access error does not overwrite or generate a fresh key.
- **Verification:** Keyring tests pass and verify memory zeroing and non-divergence.

### U6. Cross-Process Document Store, File Locking & Polymorphic Values
- **Goal:** Implement persistent namespace documents (`<namespace>.json`) with atomic writes, handle-based cross-process `.lock` files, and polymorphic `ConfigurationValue` serialization.
- **Requirements:** R3, R6, R7, R9
- **Dependencies:** U1, U2, U3
- **Files:**
  - `packages/configuration/src/values/ConfigurationValue.ts`
  - `packages/configuration/src/values/ConfigurationDocumentLocalFile.ts`
  - `packages/configuration/src/values/ConfigurationStore.ts`
  - `packages/configuration/test/unit/DocumentLock.test.ts`
- **Approach:**
  1. Implement polymorphic `ConfigurationValue`: support literal unencrypted JSON values (strings, numbers, booleans, objects) and provider/encrypted entries, preserving `json` and `reauth_required` flags.
  2. Implement handle-based locking on `<namespace>.json.lock` accommodating existing `.lock` files left by .NET, applying advisory locks and holding locks across **both** reads and writes to prevent Windows atomic rename collisions.
  3. Implement exponential backoff starting at 5ms up to `lockTimeout`, honoring `AbortSignal`.
  4. Use unique temporary sibling files (`<namespace>.json.<uuid>.tmp`) for atomic writes.
- **Patterns to follow:** `NyssaAI.Configuration/src/Values/ConfigurationDocumentLocalFile.cs` and `ConfigurationValueJsonConverter.cs`
- **Test scenarios:**
  - `documentLock_existingLockFile_acquiresSuccessfully`: Re-entrant and sequential acquisition succeeds against existing non-deleted `.lock` file.
  - `documentLock_concurrentContention_retriesAndSucceeds`: Multiple contenders synchronize cleanly with backoff.
  - `documentLock_timeoutExceeded_returnsFailureResult`: Contender holding lock beyond timeout triggers failure `Result`.
  - `documentLocalFile_atomicWrite_usesUniqueTempFile`: Temp file uses unique ID and leaves destination uncorrupted on error.
  - `configurationValue_polymorphicTypes_roundtripsCorrectly`: Primitives and JSON objects serialize and parse matching .NET.
- **Verification:** Concurrent stress tests demonstrate lock mutual exclusion, cross-process compatibility, and file integrity.

### U7. Public Client API (`IConfiguration`), TS-LOG Logging & Parity Tests
- **Goal:** Compose all modules into the primary developer-facing client class with operational boundary logging and cross-platform verification.
- **Requirements:** R1, R2, R3, R4, R9
- **Dependencies:** U1, U2, U3, U4, U5, U6
- **Files:**
  - `packages/configuration/src/IConfiguration.ts`
  - `packages/configuration/src/ConfigurationOptions.ts`
  - `packages/configuration/src/logging/IConfigurationLogger.ts`
  - `packages/configuration/src/logging/NullConfigurationLogger.ts`
  - `packages/configuration/src/ConfigurationClient.ts`
  - `packages/configuration/src/index.ts`
  - `packages/configuration/test/useCase/ConfigurationClient.usecase.test.ts`
  - `packages/configuration/test/useCase/CrossPlatformParity.usecase.test.ts`
- **Approach:**
  1. Expose `IConfiguration` with `get`, `getAs<T>`, `set`, `setAs<T>`, and `remove`, all accepting `signal?: AbortSignal`.
  2. Implement structured operational logging at the client boundary (TS-LOG): emit `timestamp`, `level`, `event`, `service`, `version`, `operation`, `duration` (monotonic clock), and correlation ID.
  3. Emit degradation warnings during lock contention / backoff retries without failing business outcomes. Wrap logger sink invocations in failure containment.
  4. Test cross-platform parity by writing files in .NET format and reading them in Node.js, asserting 100% interoperability.
- **Patterns to follow:** `NyssaAI.Configuration/src/Client/ConfigurationClient.cs` and `logging.md`
- **Test scenarios:**
  - `configurationClient_endToEndWorkflow_persistsAndRetrievesSettings`: Sets plain and encrypted values, reloads client from disk, asserts identical retrieved values.
  - `configurationClient_abortSignalTriggered_rejectsOperation`: Aborting signal aborts lock wait immediately.
  - `configurationClient_lockContention_emitsDegradationWarning`: Emits warning record during backoff retry without breaking operation.
  - `crossPlatformParity_dotNetStore_readsAndDecryptsInNode`: Reads and decrypts values produced by .NET test fixtures.
- **Verification:** Full public use-case tests pass through public entry point `index.ts`.

---

## Verification Contract

Run the suite from `packages/configuration`:

```bash
# Unit test suite (fast, deterministic, no external services)
npm run test:unit

# Public use-case test suite (exercises complete file lifecycle and cross-runtime parity)
npm run test:usecase

# Full lint & TypeScript typecheck
npm run typecheck
```

---

## Definition of Done

- [ ] All 7 implementation units completed with corresponding files.
- [ ] Every `.ts` file starts with a Single Responsibility header comment and an ASCII public interface tree.
- [ ] No `any`, strict compiler options enabled, zero floating promises.
- [ ] Deterministic AES-256-GCM cipher generates and decrypts payloads matching .NET `FormatVersion 2`.
- [ ] Strict POSIX permissions (`0700` dirs / `0600` files) enforced on Unix.
- [ ] Cross-process `.lock` files protect against race conditions.
- [ ] All unit and public use-case tests pass cleanly.
