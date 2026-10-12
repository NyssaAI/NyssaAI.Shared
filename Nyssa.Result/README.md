# Nyssa.Result

This package contains values and per-operation construction APIs, so it needs no Microsoft DI registration. Its tutorials construct those values directly; service libraries use scoped DI registrations.

The failure contract every other package in this repository is built on. An operation that can fail
returns a `Result`. Calling `.Throw()` on `Result`, or `.Unwrap()` / `.ThrowIfError()`
on `Result<T>`, explicitly opts into exception-based control flow.

For a successful `Result<T>`, `Unwrap()` returns the original value, including null
when that is a successful payload. A failed result throws `ResultException` carrying
all structured errors; its message contains user messages rather than technical details.
`ThrowIfError()` retains the same behavior for existing callers.

For `Task<Result<T>>`, import `NyssaAI.Results` and await `UnwrapAsync()`:

```csharp
await using (var session = await repository.OpenSessionAsync(cancellationToken).UnwrapAsync())
{
    // Use the successfully opened session.
}
```

The extension awaits without blocking. Task exceptions and cancellation propagate
unchanged; a completed failure result throws `ResultException`. The inner `await`
opens the session; `await using` handles asynchronous disposal. Use unwrapping when
failure should throw, and retain Result checks when the caller should handle failure.

## The primary surface

Name `Result` for an operation that produces nothing, and `Result<T>` for one that produces a value.
Both are created only through named factories — there is no public constructor.

```csharp
Result<Order> found = await orders.FindAsync(id, cancellation);
if (found.IsFailure)
{
    return found.Errors;   // pass the failure up unchanged
}
```

A success carries a value and no errors. A failure carries at least one error and no value. There is
no third state, and no accessor throws for the opposite state: `Value` on a failure returns
`default` (null for a reference type), which is why `IsSuccess` and `TryGetValue`
exist. Read `Errors` directly; it is empty on success. `TryGetValue` is annotated
`[MaybeNullWhen(false)]`, so the nullable checker warns when a value is used without checking the
returned flag; `Value` itself carries no annotation, so a read
after an `IsSuccess` check stays warning-free.

**A failure is never empty.** `Failure(IEnumerable<Error>)` handed an empty list, a null list, or a
list of only nulls substitutes one sentinel error (`Unexpected`, "A failure was reported without any
error") rather than reporting success. The same holds for the exception-taking factories. The
accumulate-then-return pattern is therefore safe to shorten:

```csharp
var errors = new List<Error>();
foreach (var item in items)
    if (!Validate(item)) errors.Add(...);
return errors.Count == 0 ? Result.Success() : Result.Failure(errors);   // the Count check is yours to keep or drop
```

Both result types expose three failure factories: `Failure(string?)`, `Failure(Error?)`, and
`Failure(IEnumerable<Error?>?)`. Use a collection expression for several errors:
`Result.Failure([first, second, third])`.

Build detailed errors through `Error`, then pass them to the result:

```csharp
Result.Failure(Error.FromMessages("Could not save.", "Storage is unavailable."));
Result.Failure(Error.FromExceptions(exception, "Could not save."));
Result.Failure(Error.Canceled(canceled));
```

These replace the former two-message, first-and-rest, and exception overloads. `FromExceptions`
preserves one error per underlying failure when an exception is an aggregate. The former
`Result.Canceled` / `Result<T>.Canceled` aliases and `TryGetErrors` have been removed;
use `Error.Canceled` and `IsFailure` / `Errors` instead.

## No factory throws

Every public factory on `Error`, `Result`, and `Result<T>` normalizes a bad argument instead of
throwing, so a consumer never has to wonder which ones are throw-free:

| Argument | Normalized to |
|---|---|
| Null user message | `Error.DefaultUserMessage` |
| Null technical message | The user message |
| Undefined `ErrorCategory` value | `Unexpected` |
| Null exception, or null exception sequence | One sentinel error ("A failure was reported without any exception detail") |
| Null `Error`, or null entries in an error sequence | Dropped; an all-null or empty sequence becomes the sentinel above |
| Null `OperationCanceledException` to `Canceled` | A `Canceled` sentinel |
| A `Try` error handler that returns null | The sentinel error |

Every argument in that table is *declared* nullable, so the compiler agrees with it instead of
contradicting it: a consumer holding a possibly-null message, exception, or error passes it straight
in, with no null-forgiving operator and no warning under this repository's warnings-as-errors setting.
The one exception is the last row — the handler's declared return is an `Error` because returning one
is the contract, and the sentinel is a safety net for a caller's bug rather than a supported input.

A null *delegate* (a null action passed to `Try`, a null callback passed to `Then`) is a programming
error and is not normalized; it surfaces as a failure from inside `Try`, and as a throw from the
composition operators, the same as any other bug in caller code.

## Composing

| Operator | Traditional name | Use |
|---|---|---|
| `Then` / `ThenAsync` | `bind`, `flatMap`, `SelectMany` | Chain an operation that can itself fail |
| `Transform` / `TransformAsync` | `map`, `Select` | Change a success value; failures pass through |
| `Resolve` / `ResolveAsync` | `match`, `fold` | Eliminate the result into one type |
| `ToGeneric` | `lift` | Give a non-generic `Result` a value, so a `Result` can continue as `Result<T>` |

A failure short-circuits: `Then` and `Transform` skip their operation and propagate the errors. The
propagated error list is shared, not copied, so an n-stage chain allocates nothing per stage on the
failure path.

Error collections are read-only snapshots. Changing an input collection after constructing a result
does not change that result, and callers cannot replace or clear its exposed errors through a mutable
collection interface.

`ToGeneric` is on the non-generic `Result` only, and takes a provider rather than a value so nothing
is computed on the failure path: `Result.Success().ToGeneric(() => Load())` is a `Result<T>`, and a
failure passes straight through carrying its errors. A throw from the provider becomes a failure, the
same as in `Transform`.

## Equality

Both result types are records and compare by value: two results are equal when their values are
equal (`EqualityComparer<T>.Default`) and their errors are equal in sequence. `Error` is itself a
record with value equality over its messages, category, and captured stack text, so two
`Failure("x")` results are equal and `Assert.Equal(expected, actual)` works on either type.

## Error categories

Every `Error` carries a category so a handler can branch on the **kind** of failure without matching
message text. Choosing the right one is the difference between a caller retrying and a caller giving
up:

| Category | Meaning | Caller's reasonable response |
|---|---|---|
| `Validation` | Input did not satisfy a precondition or invariant | Fix the input; do not retry |
| `NotFound` | The requested item does not exist | Do not retry |
| `Conflict` | Conflicts with current state, such as a duplicate | Re-read, then decide |
| `Forbidden` | The caller may not do this | Do not retry |
| `Unavailable` | A fault that may clear, such as a timeout | Retry is reasonable |
| `Canceled` | The operation was cancelled | Do **not** retry — it was asked for |
| `Unexpected` | Unclassified; the default | Investigate |
| `Indeterminate` | The operation may have completed, but its outcome cannot be confirmed | Reconcile authoritative state before deciding whether to retry |

`Canceled` is deliberately separate from `Unavailable`. The two invite opposite responses, and before
it existed every stage in this repository reported a deliberate cancellation as a transient fault a
caller might reasonably retry.

Every `Error` carries two messages: `UserMessage` is safe to display, `TechnicalMessage` is for
logs. An error built from an exception copies the message chain and the stack frames out at
construction and discards the exception, so an error stays a serializable value and no framework
exception crosses into a domain or application contract. The frames it copied are on `StackTrace`,
which is null when none was captured — the runtime captures a stack trace at throw, so an exception
constructed but never thrown carries none.

### Cancellation versus timeout

A `TaskCanceledException` is not always a cancellation. `HttpClient` throws one on a *timeout* (with
an inner `TimeoutException`), and a linked timeout source produces one the caller never asked for.
Classifying every `OperationCanceledException` as `Canceled` would tell the caller not to retry the
one fault that most deserves a retry. The translation therefore reads evidence, in
`Error.FromCancellation(exception, callerToken)`:

| Evidence | Category |
|---|---|
| The caller's token (when supplied) has been cancelled, or the exception carries it | `Canceled` |
| The caller's token was supplied and did not fire | `Unavailable` — an internal timeout or linked source |
| No caller token; the exception carries an inner `TimeoutException` | `Unavailable` |
| No caller token; the exception's own token was never cancelled | `Unavailable` — nobody asked for it |
| No caller token; the exception's own token did fire | `Canceled` |

`Error.Canceled(exception)` is for the adapter that has caught an `OperationCanceledException`
with its own token in hand and knows it fired; it always reports `Canceled`.

## Guards

`Guard.Against` names the invalid state to reject. Guards return results and compose
through `Then`; they throw only when the caller explicitly invokes `.Throw()`.
Typed guards carry the validated value on success. Null guards support both reference
types and nullable value types. Validation failures use `ErrorCategory.Validation`;
Boolean guards accept an explicit category.

```csharp
Guard.Against.NullOrWhiteSpace(name, nameof(name))
     .Then(_ => Guard.Against.OutOfRange(age, 0, 150, nameof(age)))
     .Then(_ => Result<User>.Success(new User { Name = name, Age = age }));
```

`Guard.Against.PatternMismatch` bounds its own backtracking: the pattern is caller-supplied, so a runaway such as
`^(a+)+$` returns an `Unavailable` timeout failure instead of consuming a core indefinitely.
`Guard.Against.True/False(value, predicate, ...)` and the collection overload of
`Guard.Against.NullOrEmpty` run caller code (the predicate, the
enumeration); a throw from either becomes a failure through the same translation `Try` uses.

### Collection membership guards

`Against.Contains` rejects any matching item; `Against.NotContains` rejects the
absence of a match (including an empty collection). Both return the original
non-null collection with its static type preserved. Supply a typed predicate
parameter so C# can infer the element type:

```csharp
List<string>? names = ["Ada"];
List<string> validated = Guard.Against.NotContains(
    names, (string name) => name == "Ada", "Ada is required").ThrowIfError();
```

Null collections and null predicates return Validation regardless of the requested
match-failure category. Predicate and enumeration exceptions use `Result.Try`
translation. Checks short-circuit on a match and do not copy the collection. Lazy
or single-use sources are consumed up to that decision; success is not a snapshot
or a promise that the source can be enumerated again.

### Explicit throwing

```csharp
var validatedServices = Guard.Against.Null(services, nameof(services)).ThrowIfError();
Guard.Against.True(alreadyRegistered, "Unable to register the data repository",
    "A repository provider is already registered.", ErrorCategory.Conflict).ThrowIfError();
```

`Result.ThrowIfError()` does nothing on success (`Throw()` remains an alias).
`Result<T>.ThrowIfError()` returns the successful
payload, including null when `T` allows it. Both throw `ResultException` on failure.
Use the returned value from typed null guards: a result-returning check alone cannot
tell the compiler that the original variable is non-null.

`ResultException.Errors` preserves all original errors in a read-only snapshot. Its
`Message` contains only their user messages; technical details remain in the errors.
The exception captures the throw-site stack; existing error stack text is unchanged.
Default `Try`, `TryAsync`, composition, and exception-to-result factories preserve
these errors, including inside aggregate exceptions. Explicit custom error handlers
still control their own translation. `Error.FromException` describes one exception;
use `Error.FromExceptions` to recover all structured errors.

### Migrating from 0.5 to 0.6

Version 0.6.0 removes the old flat guard methods. Names now describe rejection:

| Previous call | Replacement |
|---|---|
| `Guard.NotNull` | `Guard.Against.Null` |
| `Guard.NotNullOrEmpty` / `Guard.NotEmpty` | `Guard.Against.NullOrEmpty` |
| `Guard.NotNullOrWhiteSpace` | `Guard.Against.NullOrWhiteSpace` |
| `Guard.InRange` | `Guard.Against.OutOfRange` |
| `Guard.GreaterThan` | `Guard.Against.LessThanOrEqual` |
| `Guard.GreaterThanOrEqual` | `Guard.Against.LessThan` |
| `Guard.Matches` | `Guard.Against.PatternMismatch` |
| `Guard.Condition` (both overloads) | `Guard.Against.False` |

Also available: `Against.GreaterThan` and `Against.GreaterThanOrEqual` for upper
bounds, `Against.LengthOutOfRange` for inclusive string-length bounds, and
`Against.True` for Boolean conditions or predicates whose true state is invalid.
Comparison guards reject null values. Collection guards materialize once so a
successful payload can be consumed without re-enumerating the source. Parameter
names default to `value`, or `collection` for the collection overload.

## Where `Try` belongs

`Result.Try` and `Result.TryAsync` are the **adapter boundary**. Use them where a framework or vendor
API throws — a database driver, a file system call, a JSON parser — so the exception stops there and
becomes an error.

Prefer returning failures directly in ordinary application code. At an explicitly
chosen throwing boundary, `Try` can also convert a `ResultException` back to its
original structured failure without losing error categories or messages.

Pass the caller's token when there is one: `Result.TryAsync(token => client.GetAsync(url, token),
cancellation)`. The token is handed to the operation and then used as the evidence in the table
above, so a caller cancellation and a vendor timeout come back as different categories. The
token-free overloads still work; they fall back to the evidence the exception itself carries.

`Try` translates what it catches, with two exceptions it deliberately does not swallow:
`OperationCanceledException` is classified as above rather than as an unexpected failure, and
`OutOfMemoryException` propagates — a `Result` cannot make process-level conditions recoverable.
The overloads that take an error handler classify a cancellation first and consult the handler only
for everything else.

## Layout

Six source files, flat under `src/`: `Result`, `ResultExtensions`, `Error`, `ErrorCategory`,
`Guard`, and `ResultException`. `Result<T>` and the
non-generic `Result` share one file as the designed pair the file-naming rule explicitly permits.

The package and assembly remain `Nyssa.Result`; its namespace is `NyssaAI.Results`.
Import `using NyssaAI.Results;` to use both `Result` and `Result<T>` without an alias, including
inside other `Nyssa` namespaces. Consumers upgrading from 0.4 must update their imports and
qualified type names. Version 0.5 introduces this source and binary compatibility break.

## Runnable tutorials

See the [public API tutorials](docs/tutorials/README.md) for complete public API workflows you can run and adapt.
