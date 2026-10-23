---
feature: F-002
id: UC-002
status: complete
---

# UC-002 Swallow Standard Error Stream Failures Without Throwing

## Actor

Internal Diagnostic Writer (`StderrDiagnostics.WriteLine`).

## Goal

Ensure that if standard error (`Console.Error`) is closed, redirected to a broken pipe, or throws an exception while `StderrDiagnostics` is reporting a logging failure, the exception is swallowed (except `OutOfMemoryException`) so the host application never crashes due to diagnostic reporting.

## Starting Context

`StderrDiagnostics.WriteLine(line)` has been called to report an internal file or permission diagnostic, and `Console.Error` is in a faulted, closed, or throwing state.

## Entry Route

Invoked via `StderrDiagnostics.WriteLine(line)` (directly or through `StderrDiagnostics.Error` / `StderrDiagnostics.Warning`).

## Trigger

`Console.Error.WriteLine(line)` throws an exception during diagnostic output.

## Process

```text
StderrDiagnostics writes a line when Console.Error is faulted
+-- Starting context: preformatted diagnostic line passed to StderrDiagnostics.WriteLine
+-- Entry route: StderrDiagnostics.WriteLine(line)
+-- Trigger: Console.Error.WriteLine(line) is invoked
+-- Step 1: Attempt to write line to Console.Error
|   +-- Console.Error.WriteLine(line) succeeds -> Return normally
|   \-- Console.Error.WriteLine(line) throws an exception
+-- Step 2: Evaluate exception filter
|   +-- Exception is OutOfMemoryException
|   |   \-- Do not catch -> Allow OutOfMemoryException to propagate
|   \-- Exception is any other Exception (IOException, ObjectDisposedException, etc.)
|       \-- Catch and ignore diagnostic (nowhere left to report)
\-- Success: Control returns cleanly to the caller without throwing
```

## Step Details

### Step 2: Evaluate exception filter
- `StderrDiagnostics.WriteLine` uses `catch (Exception ex) when (ex is not OutOfMemoryException)` with an empty catch block (`// Nowhere left to report to.`), preserving the runtime's ability to fail fast on catastrophic memory exhaustion while preventing any I/O failure on `stderr` from taking down the process.

## Success

The failed diagnostic write is silently dropped and execution returns normally to the caller.

## Failure Cases

- `OutOfMemoryException` thrown during `Console.Error.WriteLine`: intentionally not caught so the CLR can handle memory exhaustion.

## Consistency Check

- Unique coverage: Covers exception safety when `Console.Error` itself fails during internal diagnostic emission.
- Closest related use cases: `UC-001` covers formatting and emitting diagnostics when `Console.Error` is operational.
- Contradictions: None found.

## Acceptance

- When `Console.Error` throws `IOException` or `ObjectDisposedException`, `StderrDiagnostics.Error`, `StderrDiagnostics.Warning`, and `StderrDiagnostics.WriteLine` complete without throwing.
- When `Console.Error` throws `OutOfMemoryException`, the exception is not swallowed.
