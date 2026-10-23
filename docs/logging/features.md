# Product Feature Progress

| Delivery Phase | Complete / Total | % Complete |
|----------------|------------------|------------|
| Phase 0 - Foundation | 2 / 2 | 100% |
| Phase 1 - Core Product | 2 / 2 | 100% |
| Phase 2 - Rotation, Retention & Resilience | 2 / 2 | 100% |
| **Overall** | 6 / 6 | 100% |

---

## How to Use This Feature Inventory

This document lists product features in intended build order for the `NyssaAI.Logging` package.

* **Feature IDs:** Canonical features use the `F-Nxx` scheme, where `N` is the delivery phase and `xx` is the two-digit feature number.
* **Feature folders:** Each feature owns a folder named with the lowercase feature ID under `docs/logging/features/`, for example `docs/logging/features/f-001/`.
* **Feature files:** The main feature guide is always `feature.md` inside that folder. Use cases live beside it as `uc-001-short-slug.md`.
* **Status:** An item is marked `complete` only when the capability is implemented, documented, and verified across the unit, use-case tutorial, package surface, and Native AOT smoke suites.
* **Phases:** Features are grouped into Foundation (Phase 0), Core Product (Phase 1), and Rotation, Retention & Resilience (Phase 2).

---

## Feature Checklist Summary

* [x] F-001: Owner-Only File & Directory Access Control
* [x] F-002: Non-Throwing Standard Error Diagnostics
* [x] F-101: Logging Registration & Destination Composition
* [x] F-102: Structured JSON Record Serialization
* [x] F-201: Size-Based Log Rotation & 30-Day Backup Retention
* [x] F-202: Fault-Tolerant File Initialization, Rotation Recovery & Disposal

## Feature Inventory

### Phase 0 - Foundation

**F-001: Owner-Only File & Directory Access Control**  
Restricts newly created log directories (`0700` on Unix, protected current-user inheritable DACL on Windows) and new or pre-existing log files (`0600` on Unix, protected current-user DACL on Windows) to the current OS account while preserving permissions on pre-existing caller-supplied directories.  
[Feature Guide: Owner-Only File & Directory Access Control](features/f-001/feature.md)

**F-002: Non-Throwing Standard Error Diagnostics**  
Formats and writes internal library error (`erro:`) and warning (`warn:`) diagnostics to standard error using four-character level tags and indented continuation lines (`StderrDiagnostics`), swallowing stream exceptions so diagnostic failures never terminate the host process.  
[Feature Guide: Non-Throwing Standard Error Diagnostics](features/f-002/feature.md)

### Phase 1 - Core Product

**F-101: Logging Registration & Destination Composition**  
Provides one-call setup (`AddNyssaLogging`) and chainable `ILoggingBuilder` extensions (`WithStdErr`, `WithConsole`, `WithRotatingJsonLog`, `AddNyssaJsonFile`) that compose standard error, independent standard output (`ConsoleOutputLoggerProvider`), UTC-dated or exact-path JSON files, and external providers such as OpenTelemetry without duplicate output or option clobbering.  
[Feature Guide: Logging Registration & Destination Composition](features/f-101/feature.md)

**F-102: Structured JSON Record Serialization**  
Implements `JsonFileLoggerProvider` and `LogRecord` serialization via `Utf8JsonWriter`, capturing `Timestamp`, `Level`, `Message`, `Module` (logger category), `Operation` (`EventId.Name`), `Exception`, and typed `AdditionalFields` (excluding `{OriginalFormat}`), with per-file `LogLevel` narrowing and synchronous stream flushing.  
[Feature Guide: Structured JSON Record Serialization](features/f-102/feature.md)

### Phase 2 - Rotation, Retention & Resilience

**F-201: Size-Based Log Rotation & 30-Day Backup Retention**  
Rotates active JSON log files once they exceed 10 MiB (`RotationTriggerMB = 10`), shifts up to 5 numbered backup files (`.1` through `.5`), and prunes expired backups older than 30 days (`RetentionDays = 30`) both at initialization and during rotation using an injectable `TimeProvider`.  
[Feature Guide: Size-Based Log Rotation & 30-Day Backup Retention](features/f-201/feature.md)

**F-202: Fault-Tolerant File Initialization, Rotation Recovery & Disposal**  
Ensures unwritable log paths degrade gracefully to `NullLoggerProvider.Instance` at startup, backs off by one 10 MiB trigger window or a 1-second reopen delay (`ReopenRetryDelay`) when mid-run rotation or file reopening fails, and releases file handles cleanly on idempotent disposal.  
[Feature Guide: Fault-Tolerant File Initialization, Rotation Recovery & Disposal](features/f-202/feature.md)
