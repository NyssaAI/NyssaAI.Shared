# Tech Stack

Record high-level technology context for planning. Keep this file descriptive.

## Stack Summary

NyssaAI Configuration is a multi-language infrastructure component in the `NyssaAI.Shared` monorepo, shipping as both a Native-AOT-compatible .NET library (`NyssaAI.Configuration` v0.2.0) and an ESM TypeScript/Node.js package (`@nyssa-ai/configuration` v0.2.0 in `nyssa-ai-configuration/`). Both implementations share the same on-disk JSON schemas, cross-process locking protocol, OS permission model, and binary cryptographic wire formats.

## Languages and Runtimes

- **C# / .NET 10.0 (`NyssaAI.Configuration`):**
  - Target framework: `net10.0` with `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, and `<IsAotCompatible>true</IsAotCompatible>`.
  - Designed for Windows, Linux, and macOS, including Native AOT and trimmed deployments.
- **TypeScript 5.8 / Node.js >= 22 (`nyssa-ai-configuration`):**
  - Package name: `@nyssa-ai/configuration` (`0.2.0`), ESM (`"type": "module"`), targeting `ES2023` with `NodeNext` module resolution and strict TypeScript compiler checks (`strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`).

## Frameworks and Services

- **.NET Dependencies & Abstractions:**
  - `Microsoft.Extensions.DependencyInjection.Abstractions` (scoped registration via `AddNyssaConfiguration`).
  - `Microsoft.Extensions.Http` (`IHttpClientFactory` for OAuth token exchange/refresh).
  - `Microsoft.Extensions.Logging.Abstractions` (`ILogger` diagnostics).
  - `NyssaAI.Results` (`Result`, `Result<T>`, `ResultException`, `ErrorCategory`).
  - `System.Security.Cryptography` (`AesGcm`, `HKDF`, `RandomNumberGenerator`, `CryptographicOperations`).
  - Platform P/Invoke bindings for OS credential stores: Windows `Advapi32` (`CredReadW`, `CredWriteW`, `CredDeleteW`, `CredFree`), macOS `Security.framework` (`SecItemCopyMatching`, `SecItemAdd`, `SecItemUpdate`, `SecItemDelete`), and Linux `libsecret-1.so.0` (`secret_service_search_sync`, `secret_password_store_sync`, `secret_item_delete_sync`).
- **Node.js / TypeScript Dependencies & Abstractions:**
  - Zero mandatory runtime third-party framework dependencies for core encryption and file locking; uses built-in `node:crypto` (`createCipheriv('aes-256-gcm')`, `createDecipheriv('aes-256-gcm')`, `hkdfSync`, `randomBytes`), `node:fs/promises`, `node:http` (loopback OAuth listener), and `node:child_process` (`icacls` / platform credential helpers).

## Storage and Artifacts

- **Global / Custom Manifest (`~/.nyssa-ai/config-manifest.json`):**
  - Schema `version: 1` JSON mapping application names to `configurationDirectory`, optional `lockTimeout`, and optional `keyVault` (`name`, `kind`, `endpoint`, `credential`, `settings`, `secrets`).
  - Protected by sidecar lock file `<manifest>.lock` and atomic temp-file rename.
- **Namespace Documents (`<configurationDirectory>/<namespace>.json`):**
  - JSON object mapping configuration keys to `ConfigurationValue` envelopes (`value`, optional `provider`, optional `encrypted`, optional `reauth_required`, optional `json`).
  - Protected by per-namespace lock file `<namespace>.json.lock` and atomic temp-file rename.
- **Local Encryption (`aesLocal`):**
  - 256-bit random key stored in OS credential store at service `<ApplicationName>`, account `"configuration-key"`.
  - FormatVersion 2 binary payload encoded as unpadded Base64URL: `[0x02][12-byte nonce][16-byte GCM tag][ciphertext]`.
  - Additional Authenticated Data (AAD): `[4-byte big-endian namespace byte length][namespace UTF-8 bytes][4-byte big-endian key byte length][key UTF-8 bytes]`.
- **Recovery Handle Token (`ConfigurationRecoveryHandle`):**
  - Encrypted and authenticated envelope using an HKDF-SHA256 subkey derived from the application's 256-bit protection key, bound to the application name and canonical configuration directory.

## Tooling

- **.NET Build & Test:**
  - `dotnet build NyssaAI.Shared.slnx`
  - `dotnet test NyssaAI.Shared.slnx` or `./run-tests.ps1`
  - Testing stack: xUnit v3 (`xunit.v3`), `FluentAssertions`, `NSubstitute`, `NetArchTest.Rules`.
  - Live OS keyring suite opt-in: `NYSSA_OS_KEYRING_TESTS=1`.
- **TypeScript / Node.js Build & Test:**
  - `npm --prefix nyssa-ai-configuration run build` (`tsc -p tsconfig.build.json`)
  - `npm --prefix nyssa-ai-configuration test` (`vitest run`)
  - `npm --prefix nyssa-ai-configuration run typecheck` (`tsc --noEmit`)

## Constraints and Unknowns

- **Cross-Language Operational Parity:** `@nyssa-ai/configuration` currently implements Phase 0 (`F-001` through `F-003`) and core parts of Phase 1 (`F-101`, baseline `F-102` and `F-103`), and requires full operational parity with `NyssaAI.Configuration` across namespace DTO replacement/merge semantics, provider migration (`MoveAsync`), interactive OAuth (`IConfigurationExtensions`), cloud vault adaptation (`ICloudKeyVault`), and guarded two-phase recovery (`RecoverAsync`).
- **OS Credential Store Availability:** Startup requires an operational OS credential store (or registered `IKeyring` replacement in tests/containers); neither implementation may silently fall back to plaintext key files on disk when the default OS keyring fails.
- **Native AOT & Strict Typing:** .NET APIs must preserve `JsonTypeInfo<T>` overloads for AOT compatibility; TypeScript APIs must obey `dev-standards` (`TS-FILE`, `TS-HEADER`, `TS-STRUCTURE`, `TS-BOUNDARY`, `TS-NAME`, `TS-DATA`, `TS-STRICT`, `TS-FAILURE`, `TS-LOG`, `TS-CONSTRUCTION`, `TS-TEST`).
