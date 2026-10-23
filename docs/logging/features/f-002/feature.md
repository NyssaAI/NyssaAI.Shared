---
id: F-002
phase: 0
status: complete
---

# F-002 Non-Throwing Standard Error Diagnostics

## Summary

Provides a centralized, non-throwing internal diagnostic writer (`StderrDiagnostics`) that formats and emits library-level errors (`erro:`) and warnings (`warn:`) to `Console.Error` using a consistent two-line layout whenever a file destination cannot be initialized, rotated, reopened, permission-restricted, or written.

## User Need

Application developers and operators need immediate, human-readable visibility on standard error when a background file logging operation (such as opening a path, rotating a full file, deleting an expired backup, or setting permissions) fails — while guaranteeing that a broken log file or broken `stderr` stream never crashes the host application.

## Use Cases

- [UC-001: Report Internal File and Permission Diagnostics to Standard Error](uc-001-report-internal-file-and-permission-diagnostics-to-stderr.md)
- [UC-002: Swallow Standard Error Stream Failures Without Throwing](uc-002-swallow-stderr-stream-failures-without-throwing.md)

## Scope

- Formatting internal diagnostics via `StderrDiagnostics.Format(levelTag, subject, detail)` as `"{levelTag}: {subject}{Environment.NewLine}      {detail}"` so internal diagnostics visually match Microsoft's console logger format.
- Emitting error-level diagnostics (`levelTag = "erro"`) via `StderrDiagnostics.Error(subject, detail)` for disabled file destinations, record write failures, rotation failures, and reopen failures.
- Emitting warning-level diagnostics (`levelTag = "warn"`) via `StderrDiagnostics.Warning(subject, detail)` for permission restriction failures and expired backup deletion failures.
- Writing preformatted lines to `Console.Error.WriteLine` inside a `try/catch` block that catches all exceptions except `OutOfMemoryException`.

## Out of Scope

- Formatting ordinary application `ILogger<T>` records written to `stderr` or `stdout` (owned by Microsoft's `ConsoleLoggerProvider` and `F-101`).
- Filtering internal file diagnostics through host `LogLevel` filters (internal `StderrDiagnostics` always emit regardless of the application's configured `LogLevel`).
- Propagating `OutOfMemoryException` suppression (`OutOfMemoryException` is deliberately allowed to escape).

## Inputs

- Four-character severity tag (`"erro"` or `"warn"`).
- Subject identifier (`string`), such as the target log file path or logger category name.
- Failure detail message (`string`), typically derived from an `Exception.Message` or validation error.

## Outputs

- Two-line formatted diagnostic written to `Console.Error` using `Environment.NewLine` and a six-space continuation indent.
- Silent no-op if `Console.Error.WriteLine` throws any non-`OutOfMemoryException` exception.

## Acceptance

- `StderrDiagnostics.Format("erro", subject, detail)` produces `"erro: <subject>{Environment.NewLine}      <detail>"`.
- `StderrDiagnostics.Error` and `StderrDiagnostics.Warning` write the formatted output to `Console.Error`.
- Exceptions thrown by `Console.Error.WriteLine` (other than `OutOfMemoryException`) are caught and swallowed without throwing to callers.
- The path contract matches `.plan-product/config.json`.

## Dependencies

None (foundational capability).
