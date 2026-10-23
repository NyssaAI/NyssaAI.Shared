---
id: F-101
phase: 1
status: complete
---

# F-101 Logging Registration & Destination Composition

## Summary

Exposes the package's entire public API surface (`LoggingServiceCollectionExtensions` and `LoggingBuilderExtensions` in `NyssaAI.Logging.Configuration`), enabling one-call `IServiceCollection` setup (`AddNyssaLogging`) and fluent `ILoggingBuilder` chaining (`WithStdErr`, `WithConsole`, `WithRotatingJsonLog`, `AddNyssaJsonFile`) alongside external providers like OpenTelemetry without duplicate output or console option clobbering.

## User Need

.NET application and CLI developers need to configure `stderr`, `stdout`, dated or exact-path JSONL files, and third-party providers (such as OpenTelemetry) in a single startup chain while keeping `stdout` clean by default, avoiding duplicate console lines when methods are called repeatedly, and injecting standard `ILogger<T>` into application classes without taking a dependency on a custom logging interface.

## Use Cases

- [UC-001: Configure Standard Error and Optional File Logging in One Call](uc-001-configure-stderr-and-optional-file-logging-in-one-call.md)
- [UC-002: Chain Dated JSON File, Standard Error, Standard Output, and OpenTelemetry](uc-002-chain-dated-json-file-stderr-stdout-and-opentelemetry.md)
- [UC-003: Add Exact-Path JSON File Destinations with Per-File Minimum Levels](uc-003-add-exact-path-json-file-destinations-with-per-file-minimum-levels.md)

## Scope

- Implementing `IServiceCollection.AddNyssaLogging(filePath = null, minimumLevel = LogLevel.Information)` to set the host minimum level, configure `stderr` output via `WithStdErr()`, optionally register an exact-path JSON file via `AddNyssaJsonFile(filePath)`, and return `ILoggingBuilder` (whose `.Services` property continues DI registration).
- Implementing `ILoggingBuilder.WithStdErr()` via `builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)` so repeated calls reuse Microsoft's singleton console provider without duplicating `stderr` records.
- Implementing `ILoggingBuilder.WithConsole()` via `ConsoleOutputLoggerProvider` (`[ProviderAlias("NyssaConsole")]`) registered with `TryAddEnumerable` and isolated `ConsoleLoggerOptions` (`LogToStandardErrorThreshold = LogLevel.None`) so `stdout` and `stderr` can both be active simultaneously without option clobbering or duplicate lines.
- Implementing `ILoggingBuilder.WithRotatingJsonLog(filePath, minimumLevel = null)` to resolve the directory (defaulting bare filenames to `Directory.GetCurrentDirectory()`), prepend the opening UTC date (`yyyy.MM.dd-<fileName>`) from `TimeProvider`, and register a `JsonFileLoggerProvider` instance.
- Implementing `ILoggingBuilder.AddNyssaJsonFile(filePath, minimumLevel = null)` to register an exact-path `JsonFileLoggerProvider` instance (supporting multiple independent file registrations with distinct `minimumLevel` thresholds).
- Enforcing fail-fast argument validation (`ArgumentNullException` for null `services`/`builder`, `ArgumentException` for null/whitespace `filePath` or a path with no file name in `WithRotatingJsonLog`) and pinning the exported public type inventory to `LoggingBuilderExtensions` and `LoggingServiceCollectionExtensions` (`SourceRootContractOptOut`).

## Out of Scope

- Publishing a custom `ILogging` client interface or options record (`NyssaAI.Logging` is strictly a provider registration package).
- Bundling or configuring OpenTelemetry exporters (`AddOpenTelemetry` is chained by the caller from `OpenTelemetry.Extensions.Hosting` / `OpenTelemetry.Exporter.*`).
- Automatic midnight rollover for `WithRotatingJsonLog` date prefixes (the `yyyy.MM.dd-` prefix is fixed when the provider opens; rotation is size-based via `F-201`).

## Inputs

- `IServiceCollection` or `ILoggingBuilder` instance during application startup.
- Optional or required `filePath` (`string`) and optional `minimumLevel` (`LogLevel` / `LogLevel?`).
- Optional DI-registered `TimeProvider` (falling back to `TimeProvider.System`).

## Outputs

- Configured `ILoggingBuilder` with registered `ILoggerProvider` singletons (`ConsoleLoggerProvider` for `stderr`, `ConsoleOutputLoggerProvider` for `stdout`, and one or more `JsonFileLoggerProvider` or `NullLoggerProvider` instances for files).
- Validation exceptions (`ArgumentNullException`, `ArgumentException`) thrown synchronously during registration when arguments are invalid.

## Acceptance

- `AddNyssaLogging()` configures `stderr` at `LogLevel.Information` (or caller-supplied minimum) and returns the `ILoggingBuilder` whose `.Services` is the same `IServiceCollection`.
- Chaining `.WithRotatingJsonLog(...)`, `.WithStdErr()`, `.WithConsole()`, and `.AddOpenTelemetry(...)` delivers a logged record once to `stderr`, once to `stdout`, once to `yyyy.MM.dd-<fileName>`, and once to OpenTelemetry, even when `.WithStdErr()` and `.WithConsole()` are called multiple times.
- `WithRotatingJsonLog("file.json")` with a bare file name creates `yyyy.MM.dd-file.json` in `Directory.GetCurrentDirectory()`.
- Multiple `AddNyssaJsonFile` registrations on the same builder maintain independent per-file `LogLevel` thresholds regardless of registration order.
- `PublicTypeInventoryTests` and `PackageSurfaceTests` confirm only `LoggingBuilderExtensions` and `LoggingServiceCollectionExtensions` are exported and `SourceRootContractOptOut` is declared.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- `F-001` (`OwnerOnlyAccess` during file initialization).
- `F-002` (`StderrDiagnostics` when file provider creation fails).
- `F-102` (`JsonFileLoggerProvider` for JSONL record emission).
