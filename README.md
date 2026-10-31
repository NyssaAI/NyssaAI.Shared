# NyssaAI.Shared

Foundational libraries for building AI tools, MCP servers, CLI utilities, autonomous agents, and production services across **.NET 10**, **TypeScript / Node.js**, and **Python**.

Conventional application libraries assume long-lived servers running inside trusted network perimeters with human operators watching dashboards. Agentic tools and modern CLIs operate under tighter constraints: they start repeatedly as short-lived local processes, coordinate shared state concurrently across languages, exchange structured protocols over `stdout`, and handle credentials on multi-user or untrusted hosts. `NyssaAI.Shared` provides stable, small-footprint building blocks engineered for those exact conditions.

## Built for the Agentic Era

* **Native AOT & instant cold starts**: Every .NET package enables trim and Native AOT analyzers (`IsAotCompatible`) with zero runtime reflection traps or assembly scanning, keeping CLI binaries and MCP servers fast and self-contained.
* **Hardened local security**: Enforces owner-only permissions (`0600` files / `0700` directories on POSIX, protected current-user DACLs on Windows), OS credential-store key custody, address-bound AES-256-GCM encryption, and zero ambient environment-variable injection.
* **Polyglot wire parity**: `.NET`, `TypeScript / Node.js`, and `Python` configuration clients read and write the exact same `~/.nyssa-ai/config-manifest.json`, `<namespace>.json` envelopes, cross-process `.lock` files, and authenticated ciphertexts.
* **Protocol-safe streams**: Logging defaults to `stderr` and structured JSONL files while isolating `stdout`, preventing stray diagnostic lines from corrupting MCP, JSON-RPC, or piped CLI output.
* **Concurrent & crash-resilient state**: Kernel handle-based file locks, atomic temp-file swaps, fail-safe logging degradation, and HKDF-authenticated recovery handles protect state when parallel agents contend or tool runs are interrupted.
* **Deterministic contracts**: Categorized `Result<T>` outcomes separate user-safe messages from internal diagnostics, and explicit SQLite-to-PostgreSQL typed command contracts (`IDbGateway`) let tools scale from local files to shared databases without ORM magic.

## Packages

| Package | Runtime & Version | Primary Contract | What It Provides |
| --- | --- | --- | --- |
| [NyssaAI.Configuration](nyssa-ai-configuration-cs/README.md) | .NET 10 (`0.3.1`) | `NyssaAI.Configuration.IConfiguration` | Manifest-driven settings, OS-keyring AES-256-GCM secrets, PKCE OAuth with silent refresh, cloud vault pointers, and guarded write recovery. |
| [@nyssa-ai/configuration](nyssa-ai-configuration-ts/README.md) | TypeScript / Node.js (`0.3.1`) | `ConfigurationClient` / `IConfiguration` | Full on-disk, lock-file, OAuth, cloud vault, and AES-256-GCM cryptographic parity with `NyssaAI.Configuration`. |
| [nyssa-ai-configuration](nyssa-ai-configuration-py/README.md) | Python 3.12+ (`0.3.1`) | `ConfigurationClient` / `Configuration` | Full on-disk, lock-file, OAuth, cloud vault, and AES-256-GCM cryptographic parity with .NET and TypeScript. |
| [NyssaAI.Logging](nyssa-ai-logging-cs/README.md) | .NET 10 (`0.2.1`) | Microsoft `ILogger<T>` | Native AOT rotating JSONL files, owner-only `0600`/`0700` and Windows DACL enforcement, independent `stderr`/`stdout` routing, and non-throwing fallback. |
| [NyssaAI.DbGateway](nyssa-ai-dbgateway-cs/README.md) | .NET 10 (`0.1.0`) | `NyssaAI.DbGateway.IDbGateway` | Low-ceremony, AOT-safe typed SQL commands and queries with automatic parameter/row type coercion, transaction scopes, and paired migrations across SQLite and PostgreSQL. |
| [NyssaAI.Result](nyssa-ai-result-cs/README.md) | .NET 10 (`0.2.1`) | `NyssaAI.Results.Result`, `Result<T>`, `Error`, `Guard` | Zero-dependency railway error composition with typed `ErrorCategory` classification and strict `UserMessage` vs. `TechnicalMessage` separation. |

## Component Capabilities

### Cross-Language Configuration & Secrets
[`NyssaAI.Configuration`](nyssa-ai-configuration-cs/README.md), [`@nyssa-ai/configuration`](nyssa-ai-configuration-ts/README.md), and [`nyssa-ai-configuration`](nyssa-ai-configuration-py/README.md) implement the shared specification in [`docs/specs/config/`](docs/specs/config/README.md):
* **One read/write mental model**: Application code reads plain JSON settings, locally encrypted secrets (`"aesLocal"`), self-refreshing OAuth 2.0 PKCE grants (`"oauth"`), and external cloud vault entries through a single interface.
* **Transplant-resistant encryption**: Local secrets use FormatVersion 2 AES-256-GCM with 256-bit keys stored in Windows Credential Manager, macOS Keychain, or Linux Secret Service, binding `(namespace, key)` as Additional Authenticated Data (AAD) so ciphertexts cannot be moved across keys or namespaces.
* **Prompt-injection & environment safety**: Bootstrap paths and vault bindings come exclusively from permission-checked manifests (`~/.nyssa-ai/config-manifest.json`), never process environment variables that untrusted child processes or inputs could manipulate.
* **Two-phase recovery**: Interrupted multi-property writes and provider migrations (`MoveAsync` / `move`) emit portable, HKDF-authenticated recovery handles so callers can safely resume work without background daemons or orphaned vault secrets.

### Protocol-Safe & Owner-Restricted Logging
[`NyssaAI.Logging`](nyssa-ai-logging-cs/README.md) supplies the operational sinks omitted from `Microsoft.Extensions.Logging` while keeping application classes on standard `ILogger<T>`:
* **Clean `stdout` for tools and MCP**: `AddNyssaLogging()` routes console output to `stderr` by default and isolates `stdout` behind a separate provider so options never clobber one another.
* **Zero-reflection rotating JSONL**: Serializes structured fields via `Utf8JsonWriter` with automatic 10 MiB rotation and 30-day backup pruning—no reflection serializers and no third-party logging engine.
* **OS-level privacy**: Closes the Unix `umask` creation window with `0600` file modes (`0700` on newly created directories) and strips inherited Windows ACLs in favor of a protected current-user DACL, while leaving pre-existing shared directories untouched.
* **Fail-safe host execution**: Unwritable paths, rotation errors, or permission warnings report directly to `stderr` and never throw into application callers or abort startup. (Equivalent Node.js setup with `pino` and `pino-roll` is documented in [`nyssa-ai-logging-cs/README.md`](nyssa-ai-logging-cs/README.md#nodejs-and-typescript-applications).)

### Portable, AOT-Safe Data Access
[`NyssaAI.DbGateway`](nyssa-ai-dbgateway-cs/README.md) gives local tools and backend services a single low-ceremony typed command model across embedded **SQLite** and server-grade **PostgreSQL**:
* **Start local, scale without rewrites**: Applications define self-contained typed commands (`IDbNonQuery`, `IDbCommand<TResult>`, `IDbQuery<TRow>`) and switch between SQLite and PostgreSQL by configuration while keeping call sites unchanged.
* **Automatic dialect type coercion**: `IParameterBinder` and `IDataRecordReader` transparently coerce `Guid`, `bool`, and `DateTimeOffset` between SQLite (`TEXT`/`INTEGER`) and PostgreSQL (`uuid`/`boolean`/`timestamptz`) without runtime reflection.
* **Safe lifetimes & paired migrations**: Standalone operations manage their own connections, `ExecuteTransactionAsync` rolls back uncommitted or faulted work automatically, and paired/universal migrations validate applied history before altering schemas.

### Structured Outcomes & Safe Error Boundaries
[`NyssaAI.Result`](nyssa-ai-result-cs/README.md) provides lightweight `Result`, `Result<T>`, `Error`, and `Guard` types used across the ecosystem:
* **Actionable error categories**: `ErrorCategory` (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unavailable`, etc.) lets callers and agents decide whether to retry, prompt, or abort without brittle message-string matching.
* **Explicit disclosure control**: `Error` separates `UserMessage` (safe to return to end users or tool callers) from `TechnicalMessage` and `StackTrace` (kept for operational logs).

## Build and Test

### .NET Packages
Install the .NET SDK version pinned in [`global.json`](global.json). From the repository root:

```powershell
dotnet restore NyssaAI.Shared.slnx --locked-mode
dotnet build NyssaAI.Shared.slnx --configuration Release
./scripts/run-tests.ps1
```

`scripts/run-tests.ps1` builds current source before running all eight unit and public API tutorial suites. A build or test failure returns a failing exit code. Configuration and Logging tests allow platform-specific skips; real keyring tests need explicit opt-in, and live-service tests are excluded. The other suites fail on skips.

Repository tests use SQLite and PostgreSQL. The runner starts and removes a disposable PostgreSQL container when Docker supports Linux containers. Otherwise, it can create a temporary database cluster using PostgreSQL binaries on `PATH` or in `PGBIN`; on Windows it also checks the standard installation directory. To supply your own isolated test database, set `NYSSAAI_REPOSITORY_POSTGRES`. Tests create and drop tables, so do not use an application database.

Run package-archive and Native AOT smoke checks locally with:

```powershell
./scripts/verify-packages.ps1
```

```sh
# On Linux, with Native AOT prerequisites and NYSSAAI_REPOSITORY_POSTGRES set:
bash scripts/verify-native.sh
```

The package check inspects all four NuGet archives, then restores and runs a separate consumer with a fresh package cache. The native checks cover Logging, Configuration (including encrypted settings across process runs), and Repository across both database providers.

For one executable tutorial suite:

```powershell
dotnet run --project nyssa-ai-result-cs/test/use_case
```

### TypeScript / Node.js (`@nyssa-ai/configuration`)
Requires Node.js 24+:

```powershell
cd nyssa-ai-configuration-ts
npm ci
npm run build
npm test
```

### Python (`nyssa-ai-configuration`)
Requires Python 3.12+:

```powershell
python -m pip install -e "nyssa-ai-configuration-py[dev]"
python -m mypy nyssa-ai-configuration-py/src --strict
python -m pytest nyssa-ai-configuration-py/test
```

## Package and Repository Boundaries

Each package owns its source, unit tests (`test/unit/`), use-case tests (`test/use_case/`), and documentation. Shared cross-language wire specifications live in [`docs/specs/`](docs/specs/config/README.md), and root [`scripts/`](scripts) contains cross-package verification and test-runner scripts. Package names and versions remain independent of the repository name.

`Directory.Packages.props` specifies direct .NET dependency versions. Committed `packages.lock.json` files pin the resolved dependency graph and package hashes; CI checks them with a locked restore. After an intentional dependency update, run `dotnet restore NyssaAI.Shared.slnx --force-evaluate` and review the lock-file changes alongside the version changes.

.NET directories, projects, assemblies, and NuGet IDs use `NyssaAI.*` (`NyssaAI.Result` uses the root namespace `NyssaAI.Results`; the other .NET libraries match their package names). TypeScript publishes as `@nyssa-ai/configuration` on npm, and Python publishes as `nyssa-ai-configuration` on PyPI.

Release workflows publish via OIDC trusted publishing when matching version tags are pushed (`NyssaAI.<Package>/v<version>` for NuGet, `nyssa-ai-configuration/v<version>` or `npm/v<version>` for npm, `nyssa-ai-configuration-py/v<version>` or `py/v<version>` for PyPI, or `v<version>` across registries); CI build and pull-request workflows never publish packages.

## Licensing

These libraries are licensed under the [Apache License, Version 2.0](LICENSE.txt). Each published package includes the license.
