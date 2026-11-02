# NyssaAI.Result (Python)

`nyssa-ai-result` gives Python applications a consistent way to report whether an operation succeeded and, when it did not, what went wrong. A function returns `Result[T]` holding either the successful payload or structured errors.

The library implements Railway-Oriented Programming (ROP) with pure combinators and explicit exception boundaries, adhering to Jeremy Maynor's `dev-standards` (v0.6.5).

---

## Core Invariant: Pure Combinators vs. Explicit Exception Boundaries

> **Fail fast on programmer defects; use `Result` for expected operational outcomes.**
> Unexpected exceptions (e.g., `AttributeError`, `ZeroDivisionError`, `KeyError`, coding defects) bubble immediately.
> External fallible I/O (e.g., HTTP drivers, database adapters) must be wrapped exclusively at explicit boundaries via `Result.try_` / `Result.try_async`.

### 1. Pure Monadic Combinators (`then`, `then_async`, `transform`, `transform_async`)
Monadic chaining methods do **not** swallow or intercept unexpected exceptions in delegates:
- If the current result is a failure, errors propagate immediately without executing the delegate.
- If the current result is a success, the delegate is executed directly. Any unexpected exception bubbles up the stack immediately so unit tests and telemetry identify the defect immediately.

### 2. Explicit Exception Boundaries (`Result.try_`, `Result.try_async`)
Volatile, external fallible boundaries catch exceptions and convert them into structured failures:
- Standard translation maps exceptions to `Error` values while preserving the causal chain and stack trace.
- Custom error handlers (`error_handler: Callable[[Exception], Error]`) allow translation to specific categories.
- Cancellation evidence (`asyncio.CancelledError`, `TimeoutError`, cancellation tokens) classifies outcomes accurately as `ErrorCategory.CANCELED` vs. `ErrorCategory.UNAVAILABLE`.

---

## Quickstart

```python
from nyssa_ai_result import Error, ErrorCategory, Guard, Result, ok, fail

# 1. Success and Failure factories
res_ok = Result.success(42)
res_fail = Result.failure(Error.from_message("Entity not found", ErrorCategory.NOT_FOUND))

# Shorthand helpers
s = ok("payload")
f = fail("something broke")

# 2. Inspecting results
if res_ok:
    print(f"Value: {res_ok.value}")

if res_fail.is_failure:
    for err in res_fail.errors:
        print(f"[{err.category}] {err.user_message}: {err.technical_message}")

# 3. Monadic Chaining (Railway-Oriented Pipeline)
pipeline_result = (
    Guard.against.null_or_whitespace(username, parameter_name="username")
    .then(lambda valid_name: find_user(valid_name))
    .transform(lambda user: user.to_dto())
)

# 4. Explicit Exception Boundary for External I/O
db_result = Result.try_(
    lambda: database_driver.query_user(user_id),
    error_handler=lambda exc: Error.from_messages(
        user_message="Database service currently unavailable.",
        technical_message=f"Driver failure: {exc}",
        category=ErrorCategory.UNAVAILABLE,
    )
)
```

---

## API Overview

### `Result[T]`
- `value: T`: The success payload (meaningful only when `is_success` is True).
- `errors: tuple[Error, ...]`: Structured failures (empty on success).
- `is_success / ok: bool`: True on success, False on failure.
- `is_failure: bool`: True on failure, False on success.
- `then(next_op: Callable[[T], Result[U]]) -> Result[U]`: Monadic bind (synchronous).
- `then_async(next_op: Callable[[T], Awaitable[Result[U]]]) -> Result[U]`: Monadic bind (asynchronous).
- `transform(fn: Callable[[T], U]) -> Result[U]`: Functor projection map (synchronous).
- `transform_async(fn: Callable[[T], Awaitable[U]]) -> Result[U]`: Functor projection map (asynchronous).
- `resolve(on_success, on_failure) -> R`: Fold / match into a unified value.
- `resolve_async(on_success, on_failure) -> R`: Async fold / match.
- `throw_if_error() -> T` / `unwrap() -> T`: Returns payload on success, or raises `ResultException`.
- `try_get_value() -> tuple[bool, T | None]`: Safe extraction tuple.
- `try_(action, error_handler=None, cancellation_token=None)`: Synchronous explicit boundary.
- `try_async(action, error_handler=None, cancellation_token=None)`: Asynchronous explicit boundary.

### `Error`
- `user_message: str`: Message safe for end users.
- `technical_message: str | None`: Diagnostic details, exception types, and chained causes.
- `category: ErrorCategory`: Categorization (`UNEXPECTED`, `VALIDATION`, `NOT_FOUND`, `CONFLICT`, `FORBIDDEN`, `UNAVAILABLE`, `CANCELED`, `INDETERMINATE`).
- `stack_trace: str | None`: Captured traceback if created from an exception.
- Named factories: `from_message`, `from_messages`, `from_exception`, `from_exceptions`, `canceled`, `from_cancellation`, `from_caught`, `at_least_one`.

### `Guard.against`
Input validation guards returning `Result[T]` with `ErrorCategory.VALIDATION`:
- `null` / `none`
- `null_or_empty`
- `null_or_whitespace`
- `out_of_range(value, min_val, max_val)`
- `less_than`, `less_than_or_equal`
- `greater_than`, `greater_than_or_equal`
- `length_out_of_range(string, min_len, max_len)`
- `pattern_mismatch(string, pattern)`
- `true(cond / value, predicate)`
- `false(cond / value, predicate)`
- `contains(collection, predicate)`
- `not_contains(collection, predicate)`
