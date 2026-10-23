# Tech Stack

Record high-level technology context for planning. Keep this file descriptive.

## Stack Summary

`NyssaAI.Logging` is a standalone, Native-AOT-compatible .NET 10 library in the `NyssaAI.Shared` monorepo (`NyssaAI.Logging/src/NyssaAI.Logging.csproj`). It implements `Microsoft.Extensions.Logging.ILoggerProvider` to supply rotating JSONL file and independent console output destinations behind Microsoft's standard `ILogger<T>` and `ILoggingBuilder` abstractions.

## Languages and Runtimes

- **C# / .NET 10.0 (`NyssaAI.Logging`):**
  - Target framework: `net10.0` (`global.json` pins the .NET 10 SDK).
  - Package & assembly name: `NyssaAI.Logging` (`RootNamespace: NyssaAI.Logging`, public registration namespace: `NyssaAI.Logging.Configuration`).
  - Native AOT & trimming: `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>` enabled.
  - Cross-platform runtime support: Windows (`System.Security.AccessControl` / `WindowsIdentity` ACLs) and Unix/Linux/macOS (`UnixFileMode` `0700` / `0600`).

## Frameworks and Services

- **Direct NuGet Dependencies (pinned via `Directory.Packages.props` and `packages.lock.json`):**
  - `Microsoft.Extensions.Logging`
  - `Microsoft.Extensions.Logging.Console` (`ConsoleLoggerProvider`, `ConsoleLoggerOptions`)
  - `Microsoft.Extensions.DependencyInjection.Abstractions` (`IServiceCollection`, `ServiceDescriptor`, `TryAddEnumerable`)
- **Base Class Library (BCL) APIs:**
  - `System.Text.Json.Utf8JsonWriter` for reflection-free, AOT-safe JSONL record serialization.
  - `System.TimeProvider` (`TimeProvider.System` default, or DI-registered `TimeProvider` for deterministic timestamps, UTC date prefixes, retention cutoffs, and reopen retry backoff).
  - `System.IO.FileStream` (`FileStreamOptions` with `FileMode.Append`, `FileAccess.Write`, `FileShare.ReadWrite | FileShare.Delete`, and `UnixCreateMode = 0600` on non-Windows).
- **External Provider Interoperability:**
  - Designed to chain directly with `OpenTelemetry.Logs` (`AddOpenTelemetry`) and any other `ILoggerProvider` registered on `ILoggingBuilder`.

## Storage and Artifacts

- **Active JSONL Log Files:**
  - Exact path supplied to `AddNyssaLogging(filePath)` or `AddNyssaJsonFile(filePath)` (requires a directory component), or UTC-date-prefixed path `yyyy.MM.dd-<fileName>` created by `WithRotatingJsonLog(filePath)`.
  - Newline-delimited UTF-8 JSON (`\n` line terminator), flushed synchronously after each record under a per-`LogFile` lock.
  - Fixed property order per JSON line: `Timestamp` (ISO-8601 round-trip `"O"`), `Level`, `Message`, `Module`, `Operation`, `Exception`, `AdditionalFields`.
- **Rotated Backup Files:**
  - Up to 5 numbered sidecar backups alongside the active file: `<logPath>.1` (newest) through `<logPath>.5` (oldest), triggered when `_bytesWritten > 10,485,760` bytes (`10 MiB`).
  - Backups with `LastWriteTimeUtc` older than 30 days (`RetentionDays = 30`) are deleted at `LogFile.Initialize()` and during `Rotate()`.
- **Standard Error Diagnostic Stream (`StderrDiagnostics`):**
  - Internal library failures write two-line diagnostics to `Console.Error` (`erro: <subject>\n      <detail>` or `warn: <subject>\n      <detail>`).

## Tooling

- **Build & Restore:**
  - `dotnet restore NyssaAI.Shared.slnx --locked-mode`
  - `dotnet build NyssaAI.Shared.slnx --configuration Release`
- **Unit & Use-Case Test Suites:**
  - `./run-tests.ps1` (runs all monorepo unit and use-case suites).
  - `dotnet run --project NyssaAI.Logging/test/NyssaAI.Logging.Tests --no-build` (unit tests for `JsonFileLoggerProvider`, `LogFile`, `LoggingInstallation`, `OwnerOnlyAccess`, `PackageSurface`, and `PublicTypeInventory`).
  - `dotnet run --project test/NyssaAI.Logging.UseCaseTests --no-build` (executable tutorials in `LoggingTutorials.cs`).
- **Package & Native AOT Verification:**
  - `./test/verify-packages.ps1` (validates NuGet archive contents and `PackageConsumer`).
  - `bash test/verify-native.sh` / `test/NyssaAI.Logging.AotSmoke` (publishes and executes Native AOT smoke binary verifying chained destinations, dated files, JSON fields, per-file filters, and disposal).

## Constraints and Unknowns

- **Deliberate Contract Opt-Out (`SP9b` / `SP9h`):** `NyssaAI.Logging.csproj` declares `<SourceRootContractOptOut>` and exports only `LoggingBuilderExtensions` and `LoggingServiceCollectionExtensions` (pinned by `PackageSurfaceTests` and `PublicTypeInventoryTests`). No `Client/` or `Contracts/` directory may be added unless the architectural contract changes.
- **No Reflection Serialization:** `LogFile.WriteField` explicitly matches `null`, `string`, `bool`, `int`, `long`, `double`, `float`, `decimal`, `DateTime`, `DateTimeOffset`, and `Guid`, falling back to `value.ToString()` so Native AOT trimming never strips runtime type metadata.
- **Process-Local Write Lock:** `LogFile` serializes writes within a single provider instance via an in-process lock and seeks to `SeekOrigin.End` before writing; separate processes or duplicate file providers targeting the same file path are unsupported for coordinated rotation.
- **Node.js / TypeScript Ecosystem Choice:** Unlike `NyssaAI.Configuration`, `NyssaAI.Logging` has no companion `@nyssa-ai/logging` Node.js package. Node.js applications use `pino` (with `destination: 2` for `stderr`) and `pino-roll` for rotating JSONL files, documented in `NyssaAI.Logging/README.md`.
