# NyssaAI.Result (TypeScript)

`nyssa-ai-result` gives TypeScript applications a consistent way to report whether an operation succeeded and, when it did not, what went wrong. A function returns `Result<T, E>` holding either the successful payload (`Ok<T>`) or structured errors (`Err<E>`).

The library implements Railway-Oriented Programming (ROP) with pure combinators and explicit exception boundaries, adhering to Jeremy Maynor's `dev-standards` (v0.6.3).

---

## Core Invariant: Pure Combinators vs. Explicit Exception Boundaries

> **Fail fast on programmer defects; use `Result` for expected operational outcomes.**
> Unexpected exceptions (e.g., `TypeError`, `ReferenceError`, `RangeError`, coding defects) bubble immediately.
> External fallible I/O (e.g., HTTP drivers, database adapters, filesystem operations) must be wrapped exclusively at explicit boundaries via `Result.try` / `Result.tryAsync` (or `try_` / `tryAsync`).

### 1. Pure Monadic Combinators (`then`, `thenAsync`, `transform` / `map`, `transformAsync` / `mapAsync`)
Monadic chaining methods do **not** swallow or intercept unexpected exceptions in callbacks:
- If the current result is a failure (`ok === false`), errors propagate immediately without executing the callback.
- If the current result is a success (`ok === true`), the callback is executed directly. Any unexpected exception bubbles up the stack immediately so unit tests and telemetry identify the defect immediately.

### 2. Explicit Exception Boundaries (`Result.try` / `try_`, `Result.tryAsync` / `tryAsync`)
Volatile, external fallible boundaries catch exceptions and convert them into structured failures:
- Standard translation maps exceptions to immutable `Error` value objects while preserving the causal chain (`cause`) and stack trace without retaining live exception references.
- Custom error handlers (`errorHandler: (error: unknown) => Error`) allow translation to specific categories.
- Cancellation evidence (`AbortSignal`, `TimeoutError`, cancellation tokens) classifies outcomes accurately as `ErrorCategory.Canceled` vs. `ErrorCategory.Unavailable`.

---

## Quickstart

```typescript
import { Error, ErrorCategory, Guard, Result, ok, fail, try_ } from "nyssa-ai-result";

// 1. Success and Failure factories
const resOk = Result.ok(42);
const resFail = Result.fail(Error.fromMessage("Entity not found", ErrorCategory.NotFound));

// Shorthand helpers
const s = ok("payload");
const f = fail("something broke");

// 2. Discriminated Union Narrowing
if (resOk.ok) {
  console.log(`Value: ${resOk.value}`);
}

if (!resFail.ok) {
  for (const err of resFail.errors) {
    console.log(`[${err.category}] ${err.userMessage}: ${err.technicalMessage}`);
  }
}

// 3. Monadic Chaining (Railway-Oriented Pipeline)
const pipelineResult = Guard.against
  .nullOrWhitespace(username, "username")
  .then((validName) => findUser(validName))
  .transform((user) => user.toDto());

// 4. Explicit Exception Boundary for External I/O
const dbResult = try_(
  () => databaseDriver.queryUser(userId),
  (exc) =>
    Error.fromMessages(
      "Database service currently unavailable.",
      `Driver failure: ${String(exc)}`,
      ErrorCategory.Unavailable
    )
);
```

---

## API Overview

### `Result<T, E = Error> = Ok<T> | Err<E>`
- `ok: boolean`: Discriminator (`true` on `Ok<T>`, `false` on `Err<E>`).
- `value: T`: The success payload on `Ok<T>`.
- `error: E`: The primary error on `Err<E>`.
- `errors: readonly Error[]`: Structured failures (empty on `Ok<T>`).
- `isSuccess / isFailure: boolean`: Boolean status flags.
- `then(nextOperation)`: Monadic bind (synchronous, pure).
- `thenAsync(nextOperation)`: Monadic bind (asynchronous, pure).
- `transform(fn)` / `map(fn)`: Functor projection map (synchronous, pure).
- `transformAsync(fn)` / `mapAsync(fn)`: Functor projection map (asynchronous, pure).
- `resolve(onSuccess, onFailure)`: Fold / match into a unified value.
- `resolveAsync(onSuccess, onFailure)`: Async fold / match.
- `throwIfError()` / `unwrap()`: Returns payload on success, or throws `ResultException`.
- `tryGetValue()`: Safe extraction tuple `readonly [true, T] | readonly [false, undefined]`.
- `try_(action, errorHandler?, cancellationToken?)`: Synchronous explicit boundary.
- `tryAsync(action, errorHandler?, cancellationToken?)`: Asynchronous explicit boundary.

### `Error`
- `userMessage: string`: Message safe for end users.
- `technicalMessage: string`: Diagnostic details, exception types, and chained causes.
- `category: ErrorCategory`: Categorization (`Unexpected`, `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unavailable`, `Canceled`, `Indeterminate`).
- `stackTrace: string | null`: Captured stack trace if created from an exception.
- `causeType: string | null`: Captured exception constructor/type name.
- Named factories: `fromMessage`, `fromMessages`, `fromException`, `fromExceptions`, `canceled`, `fromCancellation`, `fromCaught`, `atLeastOne`.

### `Guard` / `Guard.against`
Input validation guards returning `Result<T, Error>` with `ErrorCategory.Validation`:
- `null` / `none` / `againstNull`
- `nullOrEmpty` / `empty` / `againstEmpty`
- `nullOrWhitespace` / `nullOrWhiteSpace` / `againstNullOrWhitespace`
- `outOfRange(value, minVal, maxVal)` / `againstOutOfRange`
- `lessThan`, `lessThanOrEqual`
- `greaterThan`, `greaterThanOrEqual`
- `lengthOutOfRange(value, minLength, maxLength)`
- `patternMismatch(value, pattern)`
- `true(conditionOrValue, predicateOrMessage)`
- `false(conditionOrValue, predicateOrMessage)`
- `contains(collection, predicate, message)`
- `notContains(collection, predicate, message)`
