# Product Roadmap

This document lists work slices in delivery order. [docs/logging/features.md](features.md) owns the feature inventory, phases, and status; this roadmap owns dependency-driven delivery order and slicing for `NyssaAI.Logging`.

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
* **Coverage:** Every feature not yet complete in [docs/logging/features.md](features.md) needs at least one slice here. Completed features and non-feature capability entries are exempt.

---

## Slices

- [x] R-001: Implement non-throwing standard error diagnostics formatter and writer (`StderrDiagnostics`) — [F-002](features/f-002/feature.md)
    - Format internal library error (`erro:`) and warning (`warn:`) diagnostics with a 4-character level tag, subject path or category, and 6-space indented continuation line using platform line endings.
    - Swallow non-`OutOfMemoryException` stream failures when writing to `Console.Error` so diagnostic reporting never crashes the host application.
    - Verify: run `dotnet test nyssa-ai-logging-cs/test/NyssaAI.Logging.Tests --filter FullyQualifiedName~JsonFileLoggerProviderTests` and confirm unwritable path diagnostics emit `erro: relative.log` on `stderr` without throwing.
- [x] R-002: Enforce owner-only file and directory access controls across Windows and Unix (`OwnerOnlyAccess`) — [F-001](features/f-001/feature.md) — after: R-001
    - Restrict newly created log directories (`0700` on Unix, protected current-user inheritable `FullControl` DACL on Windows) while leaving pre-existing caller-supplied directories untouched.
    - Restrict newly created and pre-existing log files (`0600` on Unix via `UnixCreateMode` and `SetUnixFileMode`; protected current-user `FullControl` DACL on Windows) and report permission failures via `StderrDiagnostics.Warning`.
    - Verify: run `dotnet test nyssa-ai-logging-cs/test/NyssaAI.Logging.Tests --filter FullyQualifiedName~OwnerOnlyAccessTests` and confirm new directories/files are restricted to the current user while existing directories keep their permissions.
- [x] R-003: Implement AOT-safe structured JSONL record serialization and per-file level filtering (`JsonFileLoggerProvider`, `LogRecord`, `LogFile`) — [F-102](features/f-102/feature.md) — after: R-001, R-002
    - Serialize log records to single-line UTF-8 JSON via `Utf8JsonWriter` with fixed property order (`Timestamp`, `Level`, `Message`, `Module`, `Operation`, `Exception`, `AdditionalFields`), preserving typed scalars and dropping `{OriginalFormat}`.
    - Apply per-file `LogLevel` thresholds after host filtering, seek to `SeekOrigin.End` under a per-file lock, and flush each line synchronously.
    - Verify: run `dotnet test test/NyssaAI.Logging.UseCaseTests --filter FullyQualifiedName~Log_Structured_Messages` with `EventId(10, "OrderAccepted")` and order placeholders (`1042`, `39.50m`, `"Seattle"`), confirming the JSON line contains `Operation: "OrderAccepted"` and typed `AdditionalFields`.
- [x] R-004: Implement 10 MiB size-based file rotation and 30-day backup retention (`LogFile`) — [F-201](features/f-201/feature.md) — after: R-003
    - Rotate the active log file before writing when `_bytesWritten` exceeds 10 MiB (`RotationTriggerMB = 10`), shifting backups `.4 -> .5` down to `.1` and opening a fresh owner-restricted active file.
    - Delete numbered backups (`.1` through `.5`) with `LastWriteTimeUtc` older than 30 days (`RetentionDays = 30`) during `Initialize()` and `Rotate()` using the injected `TimeProvider`.
    - Verify: run `dotnet test nyssa-ai-logging-cs/test/NyssaAI.Logging.Tests --filter FullyQualifiedName~LogFileTests` and confirm rotation shifts `.1`–`.5` backups at threshold and removes backups older than 30 days.
- [x] R-005: Implement fault-tolerant initialization, rotation backoff recovery, and idempotent disposal (`JsonFileLoggerProvider`, `LogFile`) — [F-202](features/f-202/feature.md) — after: R-004
    - Fall back to `NullLoggerProvider.Instance` and log to `StderrDiagnostics.Error` when `JsonFileLoggerProvider.TryCreate` cannot initialize the file path.
    - Reopen the active file and defer the next rotation attempt by one trigger window (`+10 MiB`) when `Rotate()` fails, or back off reopen attempts by 1 second (`ReopenRetryDelay`) during temporary outages, and guard `Dispose()` against late reopen or repeated disposal.
    - Verify: run `dotnet test nyssa-ai-logging-cs/test/NyssaAI.Logging.Tests --filter FullyQualifiedName~WriteRecord_WhenRotation` and confirm rotation/reopen failures report once per backoff window and resume logging once file access is restored.
- [x] R-006: Expose one-call and chainable destination registration extensions (`LoggingServiceCollectionExtensions`, `LoggingBuilderExtensions`, `ConsoleOutputLoggerProvider`) — [F-101](features/f-101/feature.md) — after: R-003, R-005
    - Provide `AddNyssaLogging` on `IServiceCollection` and `WithStdErr`, `WithConsole`, `WithRotatingJsonLog` (`yyyy.MM.dd-<name>`), and `AddNyssaJsonFile` on `ILoggingBuilder`, keeping public exports strictly limited to those two static classes.
    - Isolate `stdout` routing in `ConsoleOutputLoggerProvider` (`TryAddEnumerable`) so chaining `WithStdErr()`, `WithConsole()`, dated/exact JSON files, and `AddOpenTelemetry()` writes once to each destination, and verify under Native AOT (`NyssaAI.Logging.AotSmoke`).
    - Verify: run `dotnet run --project test/NyssaAI.Logging.UseCaseTests` and `dotnet run --project test/NyssaAI.Logging.AotSmoke` and confirm all 7 use-case tutorials and Native AOT smoke checks pass.
