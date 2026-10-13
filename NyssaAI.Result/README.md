# NyssaAI.Result

`NyssaAI.Result` gives .NET applications a consistent way to report whether an operation succeeded and, when it did not, what went wrong. A method returns `Result` when it has no value to return, or `Result<T>` when it produces a value. The caller can handle the failure, pass it to another method, or choose to throw an exception.

The library provides the result and error types used throughout NyssaAI.Shared. It requires no service registration or configuration. The package targets .NET 10 and enables Native AOT compatibility checks.

## Simple results, consistent error handling

Return a result from a method that can fail, then let the caller decide what to do with it. The same approach works for validation, application operations, and calls to external services.

- **Report success.** Use `Result.Success()` for an operation with no return value, or `Result<T>.Success(value)` to return a value.
- **Describe a failure.** Use `Failure` with a message, an `Error`, or a collection of errors.
- **Handle the result.** Check `IsSuccess` or `IsFailure` before using the value. Errors are available through `Errors`.
- **Connect dependent operations.** Use `Then` to run the next operation only when the previous one succeeds.
- **Choose when to throw.** Use `ThrowIfError()` when a failure should stop execution with an exception.

Import the namespace once in the files that use these types:

```csharp
using NyssaAI.Results;
```

### Example: return an order or a failure

Here, `order` is an existing `Order` object. These are separate examples of what an order lookup could return:

```csharp
return Result<Order>.Success(order);
```

```csharp
return Result<Order>.Failure(Error.FromMessage("Order not found.", ErrorCategory.NotFound));
```

The caller checks the result before using the order:

```csharp
var result = await orders.FindAsync(id);
if (result.IsFailure)
    return Result.Failure(result.Errors);

var order = result.Value;
```

This example is inside a method that returns `Result`. Passing `result.Errors` to `Failure` keeps the original messages and categories. If the calling method returns a value, use its result type, such as `Result<Receipt>.Failure(result.Errors)`.

## Result and Result<T>

A result has two possible states: success or failure. A successful `Result<T>` holds the returned value and has no errors. A failed result holds at least one error. On a failed `Result<T>`, `Value` is `default(T)`. Create results through `Success` and `Failure`; there are no public constructors.

The main members are:

| Member | What it does |
| --- | --- |
| `IsSuccess` / `IsFailure` | Reports the outcome. |
| `Value` | Returns the value from a successful `Result<T>`. |
| `Errors` | Returns the failure's errors, or an empty list on success. |
| `TryGetValue(out value)` | Checks success and retrieves the value in one call. Available on `Result<T>`. |
| `ThrowIfError()` | Throws on failure. For `Result<T>`, returns the value on success. |
| `Then` / `ThenAsync` | Runs another operation that returns a result. |
| `Transform` / `TransformAsync` | Changes the value in a successful `Result<T>`. |
| `Resolve` / `ResolveAsync` | Handles either outcome and produces application output or runs an action. |
| `ToGeneric` | Gives a successful non-generic `Result` a value. |

Reading `Value` on a failure does not throw. It returns the type's default value, which can look like valid data, such as `0` or `null`. Check the result first, or use `TryGetValue`:

```csharp
if (result.TryGetValue(out var order))
    Console.WriteLine(order.Id);
```

Success can also carry `null` when the payload type allows it. A successful result means the operation succeeded; it does not guarantee a non-null value.

## Error - The failure details

An [`Error`](src/Error.cs) describes one failure. It separates the message shown to the user from the details needed to investigate the problem:

- **`UserMessage`** is the message intended for the user.
- **`TechnicalMessage`** contains diagnostic details for logs.
- **`Category`** identifies the kind of failure.
- **`StackTrace`** contains captured stack text when the error was created from a thrown exception.

Use one message when it is suitable for both the user and the logs:

```csharp
var error = Error.FromMessage("An email address is required.", ErrorCategory.Validation);
```

Use separate messages when the diagnostic details should stay in the logs:

```csharp
var error = Error.FromMessages("Could not save the order.", "The storage service is unavailable.", ErrorCategory.Unavailable);
```

The library stores the messages you supply; it does not remove sensitive information from them. Choose the user message accordingly.

### Return several errors

Both result types accept a single message, a single `Error`, or a collection of errors:

```csharp
return Result.Failure([nameError, emailError]);
```

A failure always contains at least one error. Passing an empty, null, or all-null error collection creates a fallback `Unexpected` error. If you collect validation errors, keep the success check:

```csharp
return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
```

### Error categories

Use [`ErrorCategory`](src/ErrorCategory.cs) to decide how to handle a failure without comparing message text. The category describes the outcome; the caller still decides whether a retry is appropriate.

| Category | Meaning | Typical response |
| --- | --- | --- |
| `Validation` | The input does not meet a requirement. | Correct the input. |
| `NotFound` | The requested item does not exist. | Check the request or report the missing item. |
| `Conflict` | The operation conflicts with the current state. | Read the current state before trying again. |
| `Forbidden` | The caller is not permitted to perform the operation. | Resolve the access problem. |
| `Unavailable` | A temporary problem, such as a timeout, prevented completion. | Consider a retry if the operation can safely be repeated. |
| `Canceled` | The operation was canceled at the caller's request. | Stop the work. |
| `Unexpected` | The failure has not been classified. This is the default. | Investigate the diagnostic details. |
| `Indeterminate` | The operation may have completed, but its outcome is unknown. | Confirm the actual state before repeating the operation. |

## Connect operations

Use `Then` when one operation depends on the successful result of another. Use `Transform` when you only need to calculate or select a value.

```csharp
var total = Guard.Against.OutOfRange(quantity, 1, 100)
    .Transform(count => count * unitPrice);
```

Here, `total` is a result containing the calculated amount. An invalid quantity returns a validation failure, and the calculation is skipped.

`Then` can connect multiple validation steps in the same way:

```csharp
var validated = Guard.Against.NullOrWhiteSpace(name)
    .Then(_ => Guard.Against.OutOfRange(quantity, 1, 100));
```

Each step runs only after success. The first failure passes through the remaining steps with its errors intact. This stops at the first failure; it does not collect errors from steps that were skipped.

Use `Resolve` to turn either outcome into a value your application can use:

```csharp
var message = total.Resolve(
    amount => $"Total: {amount}",
    errors => errors[0].UserMessage);
```

`ThenAsync`, `TransformAsync`, and `ResolveAsync` accept callbacks that return `Task`. Call them on the result after awaiting the operation that produced it. A non-generic `Result` can also acquire a value through `ToGeneric(() => value)`; the callback runs only on success.

Exceptions from `Then`, `Transform`, and `ToGeneric` callbacks become failures, except for `OutOfMemoryException`. `ThenAsync` and `TransformAsync` follow the same rule. Exceptions from `Resolve` callbacks propagate to the caller, as do exceptions from custom `Try` error handlers.

## Validate input with Guard

[`Guard.Against`](src/Guard.cs) names the condition to reject. For example, `NullOrWhiteSpace` rejects a missing name, and `OutOfRange` rejects a quantity outside the supplied bounds.

```csharp
var nameResult = Guard.Against.NullOrWhiteSpace(name, nameof(name));
var quantityResult = Guard.Against.OutOfRange(quantity, 1, 100, nameof(quantity));
```

Guards return results. Typed guards carry the validated value on success, so they can be used with the same checks and chaining methods as any other result.

| Guard | Rejects |
| --- | --- |
| `Null` | A null reference or nullable value. |
| `NullOrEmpty` | A null or empty string or collection. |
| `NullOrWhiteSpace` | A null, empty, or whitespace-only string. |
| `OutOfRange` | A value outside the inclusive minimum and maximum. |
| `LessThan` / `LessThanOrEqual` | A value below, or at or below, the supplied limit. |
| `GreaterThan` / `GreaterThanOrEqual` | A value above, or at or above, the supplied limit. |
| `LengthOutOfRange` | A string outside the inclusive length limits. |
| `PatternMismatch` | A string that does not match a regular expression. |
| `True` / `False` | A Boolean condition, or a predicate result, matching the named state. |
| `Contains` / `NotContains` | A collection with a matching item, or without a matching item. |

Validation failures use `ErrorCategory.Validation` by default. Boolean and membership guards also accept a category for the condition being checked. Comparison guards reject null values. Parameter names default to `value`, or `collection` for the collection overload of `NullOrEmpty`.

`PatternMismatch` limits matching to 250 milliseconds. An invalid pattern returns `Validation`; a match timeout returns `Unavailable`. Exceptions from predicates or collection enumeration are translated through `Result.Try`.

### Check a collection

Membership guards use a predicate to identify the item. Give the predicate parameter a type so C# can infer the element type:

```csharp
var result = Guard.Against.NotContains(names, (string name) => name == "Ada", "Ada is required.");
```

`Contains` and `NotContains` return the original collection with its type preserved. They stop checking once a match is found and do not copy the collection. A lazy or single-use source may therefore be partly or fully consumed. Null collections and null predicates return `Validation`, regardless of the requested category.

The collection overload of `NullOrEmpty` works differently: it reads the collection into a list and returns that list, so the successful value can be used without reading the source again.

## Convert exceptions into results

Use `Result.Try` or `Result.TryAsync` when calling an API that throws, such as file access, a database driver, or a JSON parser. The operation runs inside the call, and a caught exception becomes a failed result.

```csharp
var result = Result.Try(() => File.ReadAllText(path));
```

For asynchronous calls, pass the caller's cancellation token when one is available:

```csharp
var result = await Result.TryAsync(token => File.ReadAllTextAsync(path, token), cancellationToken);
```

The overload determines whether the result contains a value. An action returns `Result`; a function returning `T` produces `Result<T>`. Use direct `Success` and `Failure` returns for ordinary application outcomes.

A custom error handler lets you supply a user message and category:

```csharp
var result = Result.Try(() => File.ReadAllText(path),
    exception => Error.FromException(exception, "Could not read the file."));
```

`Error.FromException` describes one exception. `Error.FromExceptions` expands aggregate exceptions into separate errors and preserves the original errors carried by a `ResultException`, including one inside an aggregate. These factories copy the diagnostic messages and available stack traces; they do not retain the exception object. An exception that has never been thrown normally has no stack trace to copy.

The default `Try` methods use this translation automatically. `OutOfMemoryException` propagates. A custom handler controls its own translation, but cancellation is classified before that handler is called.

### Cancellation and timeouts

A cancellation exception can mean that the caller canceled the operation, or that an internal timeout stopped it. The library uses `Error.FromCancellation(exception, callerToken)` to distinguish these cases.

The rules are applied in this order:

1. If a caller token was supplied, a requested cancellation or an exception carrying that same cancelable token produces `Canceled`. Other cancellation exceptions produce `Unavailable`.
2. Without a caller token, an inner `TimeoutException` produces `Unavailable`.
3. Otherwise, the exception's token determines the category: a canceled token produces `Canceled`; a token that was not canceled produces `Unavailable`.

`Try` and `TryAsync` overloads that accept a token pass it to the operation and use it for this classification. Token-free overloads use the evidence on the exception. Use `Error.Canceled(exception)` when you already know the caller requested cancellation; it always produces `Canceled`.

## Choose when a failure should throw

Use `ThrowIfError()` when the caller wants to stop with an exception instead of handling a failed result. For `Result<T>`, it also returns the successful value:

```csharp
var order = result.ThrowIfError();
```

The same call works with typed guards. Use the returned value so the compiler knows a null check succeeded:

```csharp
var validName = Guard.Against.Null(name, nameof(name)).ThrowIfError();
```

`Unwrap()` is equivalent to `ThrowIfError()` on `Result<T>`. `Throw()` is an alias on non-generic `Result` and does nothing on success. Successful null payloads remain null when unwrapped.

For a `Task<Result<T>>`, use `UnwrapAsync()`:

```csharp
var order = await orders.FindAsync(id).UnwrapAsync();
```

`UnwrapAsync` awaits the task and returns the successful value. A completed failure result throws [`ResultException`](src/ResultException.cs). Exceptions and cancellation from the task itself propagate unchanged. The extension is defined for `Task<Result<T>>`; for a `ValueTask<Result<T>>`, await the operation and call `ThrowIfError()` on its result.

`ResultException.Errors` retains all the original errors in a read-only snapshot. Its `Message` contains their user messages. Technical messages and captured stack text remain available through `Errors`, and the exception captures its own stack when thrown.

Default `Try` calls and result-producing composition methods can convert a `ResultException` back to its original errors. This lets code return to result-based handling after an explicitly chosen throwing call.

## How the library is organized

The library contains values and static helpers. There is no client, provider, or backing store to initialize.

- **[`Result` and `Result<T>`](src/Result.cs)** carry the outcome and provide methods for checking or connecting operations.
- **[`Error`](src/Error.cs)** holds one failure's messages, category, and captured stack text.
- **[`ErrorCategory`](src/ErrorCategory.cs)** identifies the kind of failure.
- **[`Guard`](src/Guard.cs)** checks input and returns results.
- **[`ResultException`](src/ResultException.cs)** carries errors when the caller chooses to throw.
- **[`ResultExtensions`](src/ResultExtensions.cs)** provides asynchronous unwrapping.

Results and errors are immutable records. Error collections are read-only snapshots; changing the original collection does not change the result. Chained failures reuse the existing error list. A successful payload is the original value, so returning a mutable object does not make that object immutable.

Results compare using the payload type's default equality and the errors in sequence. Errors compare their messages, category, and captured stack text. Two independently created `Result.Failure("Could not save.")` values therefore compare equal.

### Missing error details

The construction methods accept missing messages and error details without turning a failure into success:

| Input | Result |
| --- | --- |
| Null user message | `Error.DefaultUserMessage`. |
| Null technical message | The user message. |
| Undefined category | `Unexpected`. |
| Null exception, or null or empty exception collection | An error explaining that no exception detail was supplied. |
| Null error, or null, empty, or all-null error collection | An `Unexpected` error explaining that no error was supplied. |
| Null cancellation exception | A `Canceled` error. |
| Custom `Try` handler returning null | The fallback error for missing error details. |

Null entries in error collections are ignored. These fallbacks do not guarantee that every call is exception-free: caller-supplied enumeration can throw, and callbacks follow the handling rules described above. Supply valid delegates; null callbacks are programming errors.

## Migrating existing callers

Version 0.5 changed the namespace to `NyssaAI.Results`. The package and assembly remain `NyssaAI.Result`. Callers upgrading from 0.4 must update imports and qualified type names and rebuild.

Version 0.6 replaced the flat guard methods with `Guard.Against` methods that name the condition to reject:

| Previous call | Replacement |
| --- | --- |
| `Guard.NotNull` | `Guard.Against.Null` |
| `Guard.NotNullOrEmpty` / `Guard.NotEmpty` | `Guard.Against.NullOrEmpty` |
| `Guard.NotNullOrWhiteSpace` | `Guard.Against.NullOrWhiteSpace` |
| `Guard.InRange` | `Guard.Against.OutOfRange` |
| `Guard.GreaterThan` | `Guard.Against.LessThanOrEqual` |
| `Guard.GreaterThanOrEqual` | `Guard.Against.LessThan` |
| `Guard.Matches` | `Guard.Against.PatternMismatch` |
| `Guard.Condition` | `Guard.Against.False` |

For older result construction calls, use `Error.FromMessages` for separate messages, `Error.FromExceptions` for exceptions, and a collection passed to `Failure` for multiple errors. Replace the removed `Result.Canceled` aliases with `Result.Failure(Error.Canceled(exception))`, using `Result<T>.Failure` for a typed result. Replace `TryGetErrors` with `IsFailure` and `Errors`.

## Run the tutorials and tests

The [executable tutorials](docs/tutorials/README.md) cover validation, chained operations, explicit throwing, file failures, and cancellation. Their [source](../test/NyssaAI.Result.UseCaseTests/ResultTutorials.cs) shows complete public API examples without external service dependencies.

Run the tutorials from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project test/NyssaAI.Result.UseCaseTests
```

Run the Result unit tests:

```powershell
dotnet run --project NyssaAI.Result/test/NyssaAI.Result.Tests
```

## Package and licensing

The package and assembly are named `NyssaAI.Result`. Its public types use the `NyssaAI.Results` namespace.

Refer to the repository's [licensing notice](../README.md#licensing) and [LICENSE.txt](../LICENSE.txt) for terms of use.
