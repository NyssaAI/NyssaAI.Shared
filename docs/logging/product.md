# Product

## Purpose

NyssaAI Logging (`NyssaAI.Logging`) is a lightweight, Native-AOT-compatible .NET 10 logging destination library for `Microsoft.Extensions.Logging`. It gives applications, command-line tools, and AI agents a zero-ceremony way to configure standard error (`stderr`), standard output (`stdout`), and owner-restricted rotating JSONL log files while consuming the standard `ILogger<T>` interface with no custom logging wrapper or configuration dependency.

## Audience

- **Application and CLI Engineers:** Developers building .NET services, command-line utilities, and Native AOT binaries in the NyssaAI ecosystem who want ordinary `ILogger<T>` injection with clean `stderr`/`stdout` separation and local rotating JSON logs.
- **AI Agent & Tool Builders:** Engineers building MCP servers, CLI tools, or automated harnesses where `stdout` must remain reserved for structured protocol or command output while diagnostics go to `stderr` and searchable structured records persist to disk.
- **System Operators & Observability Engineers:** Operators and log shippers ingesting newline-delimited JSON (`JSONL`) records or chaining `OpenTelemetry.Logs` exporters alongside local file and terminal sinks.

## Problem

Applications and CLI tools built on `Microsoft.Extensions.Logging` face several recurring gaps when setting up local and terminal logging:

- **Standard output pollution:** Microsoft's default console logger writes `Information` and `Warning` records to `stdout`, corrupting machine-readable command output or piped data streams unless explicitly reconfigured.
- **Console provider option clobbering:** Calling `AddConsole` twice to write to both `stderr` and `stdout` shares a single `ConsoleLoggerOptions` instance, causing the last registration to overwrite routing for both destinations.
- **No built-in rotating structured JSON file sink:** .NET includes no built-in, AOT-safe rotating JSONL file provider; third-party logging frameworks often introduce heavy dependencies, custom logger interfaces, or reflection-based serialization that breaks trimming and Native AOT.
- **Fragile filesystem and permission handling:** Naive file loggers fail application startup when a log path is unwritable, leave log files containing exception stack traces and structured data readable by other local accounts, or strip permissions from shared system directories like `/var/log` or `C:\ProgramData\Logs`.

## Solution

NyssaAI Logging provides two public registration extension classes (`LoggingServiceCollectionExtensions` and `LoggingBuilderExtensions` in `NyssaAI.Logging.Configuration`) that compose cleanly with `Microsoft.Extensions.Logging`:

- **One-call and chainable destination setup:** `services.AddNyssaLogging()` configures the minimum log level (`Information` by default), routes console logging to `stderr` via `WithStdErr()`, optionally attaches a JSON file sink, and returns `ILoggingBuilder` for chaining `.WithRotatingJsonLog(...)`, `.WithConsole()`, `.AddNyssaJsonFile(...)`, or `.AddOpenTelemetry(...)`.
- **Independent `stdout` and `stderr` routing:** `ConsoleOutputLoggerProvider` (`[ProviderAlias("NyssaConsole")]`) subclasses `ConsoleLoggerProvider` with isolated `IOptionsMonitor<ConsoleLoggerOptions>` (`LogToStandardErrorThreshold = LogLevel.None`) and registers via `TryAddEnumerable`, allowing `WithConsole()` and `WithStdErr()` to coexist without duplicating lines or clobbering options.
- **AOT-safe structured JSONL file logging:** `JsonFileLoggerProvider` and `LogFile` serialize each record as a single UTF-8 JSON line using `Utf8JsonWriter` with a fixed seven-property schema (`Timestamp`, `Level`, `Message`, `Module`, `Operation`, `Exception`, `AdditionalFields`), preserving primitive JSON types for message-template placeholders while dropping `{OriginalFormat}`.
- **Automatic size rotation and age retention:** Active log files rotate once they exceed 10 MiB (`10,485,760` bytes), shifting up to 5 numbered backups (`.1` newest through `.5` oldest) and pruning backups older than 30 days both at startup and during rotation.
- **Owner-only access and non-throwing resilience:** Newly created directories (`0700` on Unix, current-user-only inheritable DACL on Windows) and new or existing log files (`0600` on Unix, protected current-user-only DACL on Windows) are restricted to the current user while existing shared directories keep their operator-assigned permissions. Unwritable files, failed rotations, or permission errors report formatted `erro:`/`warn:` lines to `stderr` via `StderrDiagnostics` and never crash application startup or runtime callers.

## Boundaries

- **No custom logger contract or client wrapper:** The package intentionally opts out of a root client interface (`SourceRootContractOptOut` in `NyssaAI.Logging.csproj`) and exports only `LoggingServiceCollectionExtensions` and `LoggingBuilderExtensions`. Applications inject `Microsoft.Extensions.Logging.ILogger<T>`.
- **No bundled OpenTelemetry exporter or configuration store:** The package does not depend on `OpenTelemetry`, `NyssaAI.Configuration`, or `NyssaAI.Results`. Applications optionally chain `AddOpenTelemetry` or pass paths loaded from `NyssaAI.Configuration` at startup.
- **No scope capture, reflection serialization, or automatic redaction:** `JsonFileLogger.BeginScope` returns `null`, complex objects not matching known primitive/value types are formatted via `ToString()` rather than reflection-serialized, and the library performs no automatic PII redaction.
- **Single-writer lock semantics and dated base files:** File write and rotation locks are in-process per `LogFile` instance (though files open with `FileShare.ReadWrite | FileShare.Delete`), and date-prefixed base files (`yyyy.MM.dd-<name>`) from earlier process runs are not automatically deleted by the 30-day backup pruner.
- **Existing caller directories are never re-permissioned:** `OwnerOnlyAccess.CreateDirectory` never tightens permissions on a directory that already exists on disk.

## Success

- **Zero-intrusion `ILogger<T>` ergonomics:** Application classes log structured events, operations (`EventId.Name`), and exceptions through standard `ILogger<T>` calls without referencing `NyssaAI.Logging`.
- **Independent multi-destination fan-out:** Chaining `AddNyssaLogging()`, `WithRotatingJsonLog()`, `WithStdErr()`, `WithConsole()`, and `AddOpenTelemetry()` delivers each accepted record exactly once to each destination while honoring both host-level and per-file `LogLevel` thresholds.
- **Native AOT & trimming compliance:** The library compiles and runs under Native AOT (`IsAotCompatible`, `IsTrimmable`) with zero reflection serialization warnings, verified by `NyssaAI.Logging.AotSmoke`.
- **Fail-safe runtime operation:** Unwritable paths degrade to `NullLoggerProvider.Instance`, mid-run rotation failures back off by one 10 MiB trigger window or a 1-second reopen retry delay, and `StderrDiagnostics` never throws even if `Console.Error` fails.

## Features

The feature inventory, build order, and detailed feature guides are maintained in [docs/logging/features.md](features.md) and `docs/logging/features/`.

## Tech Stack

Runtime, framework, storage, tooling, and environment assumptions are maintained in [docs/logging/tech-stack.md](tech-stack.md).
