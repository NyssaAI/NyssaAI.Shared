using System.Diagnostics.CodeAnalysis;

namespace NyssaAI.Results;

/// <summary>
/// Result is a railway-oriented outcome holding either a success value or the errors that
/// prevented one. Instances are created only through the named factories.
/// </summary>
/// <remarks>
/// <para>
/// The composition operators carry the traditional functional names in their summaries so they are
/// discoverable by either vocabulary: <see cref="Then{TNext}"/> is the monadic <c>bind</c>,
/// <see cref="Transform{TOut}"/> is the functor <c>map</c>, and <see cref="Resolve{TOut}"/> is the
/// <c>match</c> or <c>fold</c> that eliminates the result.
/// </para>
/// <para>
/// A failure always carries at least one error: a factory handed an empty or null collection, or
/// only null entries, substitutes a sentinel error rather than reporting success. Two results are
/// equal when their values are equal and their errors are equal in sequence.
/// </para>
/// </remarks>
/// <typeparam name="TPayload">The type of the success value.</typeparam>
public sealed record Result<TPayload>
{
    private Result(TPayload value)
    {
        Value = value;
        Errors = [];
    }

    // Trusts its input: every caller either built the list through Error.AtLeastOne or is
    // propagating the errors of a result already known to be a failure.
    private Result(IReadOnlyList<Error> errors)
    {
        // Reading Value on a failure is a caller mistake; IsSuccess and TryGetValue exist so that
        // read never has to happen, and returning default beats throwing from a property.
        Value = default!;
        Errors = errors;
    }

    /// <summary>
    /// Gets the success value. Meaningful only when <see cref="IsSuccess"/> is true; on a failure it
    /// is <c>default</c>, which for a reference type is null. Branch on <see cref="IsSuccess"/> or
    /// use <see cref="TryGetValue"/> rather than reading it blind.
    /// </summary>
    public TPayload Value { get; }

    /// <summary>Gets the errors that caused the failure, empty on success.</summary>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Returns the success value, or explicitly throws with all structured errors on failure.</summary>
    /// <exception cref="ResultException">This result is a failure.</exception>
    public TPayload ThrowIfError()
    {
        if (IsFailure) throw new ResultException(Errors);
        return Value;
    }

    /// <summary>Returns the success value, including a successful null, or throws with all errors on failure.</summary>
    /// <remarks>This is an explicit exception boundary, equivalent to <see cref="ThrowIfError"/>.</remarks>
    /// <returns>The original successful payload.</returns>
    /// <exception cref="ResultException">This result is a failure.</exception>
    public TPayload Unwrap() => ThrowIfError();

    /// <summary>Creates a successful result carrying the given value.</summary>
    /// <param name="value">The success value.</param>
    public static Result<TPayload> Success(TPayload value) => new(value);

    /// <summary>Creates a failed result from a single message used for both contexts.</summary>
    /// <param name="message">The message for both contexts; null becomes <see cref="Error.DefaultUserMessage"/>.</param>
    public static Result<TPayload> Failure(string? message) =>
        Failure(Error.FromMessage(message));

    /// <summary>Creates a failed result from a single error.</summary>
    /// <param name="error">The error describing the failure; null is replaced by a sentinel error.</param>
    public static Result<TPayload> Failure(Error? error) => new(Error.AtLeastOne([error]));

    /// <summary>
    /// Creates a failed result accumulating several errors. An empty collection still yields a
    /// failure, carrying a sentinel error.
    /// </summary>
    /// <param name="errors">The errors describing the failure; null, empty, or all-null yields a sentinel error.</param>
    public static Result<TPayload> Failure(IEnumerable<Error?>? errors) => new(Error.AtLeastOne(errors));

    /// <summary>
    /// Chains this result into the next operation, known traditionally as the monadic <c>bind</c>
    /// (also <c>flatMap</c> or <c>SelectMany</c>). The operation is skipped and the errors
    /// propagate when this result is a failure.
    /// </summary>
    /// <param name="nextOperation">The operation to run on the success value.</param>
    /// <typeparam name="TNext">The payload type produced by the next operation.</typeparam>
    public Result<TNext> Then<TNext>(Func<TPayload, Result<TNext>> nextOperation)
    {
        if (IsFailure)
            return Result<TNext>.Propagate(Errors);

        try
        {
            return nextOperation(Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TNext>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Chains this result into the next asynchronous operation; the asynchronous
    /// <see cref="Then{TNext}"/>, known traditionally as the monadic <c>bind</c>.
    /// </summary>
    /// <param name="nextOperation">The asynchronous operation to run on the success value.</param>
    /// <typeparam name="TNext">The payload type produced by the next operation.</typeparam>
    public async Task<Result<TNext>> ThenAsync<TNext>(
        Func<TPayload, Task<Result<TNext>>> nextOperation)
    {
        if (IsFailure)
            return Result<TNext>.Propagate(Errors);

        try
        {
            return await nextOperation(Value).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TNext>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Transforms the success value, known traditionally as the functor <c>map</c> (also
    /// <c>Select</c>). The errors propagate untouched when this result is a failure.
    /// </summary>
    /// <param name="transformation">The projection to apply to the success value.</param>
    /// <typeparam name="TOut">The projected payload type.</typeparam>
    public Result<TOut> Transform<TOut>(Func<TPayload, TOut> transformation)
    {
        if (IsFailure)
            return Result<TOut>.Propagate(Errors);

        try
        {
            return Result<TOut>.Success(transformation(Value));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TOut>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Transforms the success value asynchronously; the asynchronous <see cref="Transform{TOut}"/>,
    /// known traditionally as the functor <c>map</c>.
    /// </summary>
    /// <param name="transformation">The asynchronous projection to apply to the success value.</param>
    /// <typeparam name="TOut">The projected payload type.</typeparam>
    public async Task<Result<TOut>> TransformAsync<TOut>(Func<TPayload, Task<TOut>> transformation)
    {
        if (IsFailure)
            return Result<TOut>.Propagate(Errors);

        try
        {
            var value = await transformation(Value).ConfigureAwait(false);
            return Result<TOut>.Success(value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TOut>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Eliminates the result by applying one of two functions, known traditionally as <c>match</c>
    /// or <c>fold</c>.
    /// </summary>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the accumulated errors.</param>
    /// <typeparam name="TOut">The type both branches produce.</typeparam>
    public TOut Resolve<TOut>(
        Func<TPayload, TOut> onSuccess,
        Func<IReadOnlyList<Error>, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Errors);

    /// <summary>
    /// Eliminates the result by running one of two actions, known traditionally as <c>match</c>.
    /// </summary>
    /// <param name="onSuccess">Run with the success value.</param>
    /// <param name="onFailure">Run with the accumulated errors.</param>
    public void Resolve(Action<TPayload> onSuccess, Action<IReadOnlyList<Error>> onFailure)
    {
        if (IsSuccess)
            onSuccess(Value);
        else
            onFailure(Errors);
    }

    /// <summary>
    /// Eliminates the result by applying one of two asynchronous functions; the asynchronous
    /// <see cref="Resolve{TOut}"/>, known traditionally as <c>match</c>.
    /// </summary>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the accumulated errors.</param>
    /// <typeparam name="TOut">The type both branches produce.</typeparam>
    public async Task<TOut> ResolveAsync<TOut>(
        Func<TPayload, Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure) =>
        IsSuccess
            ? await onSuccess(Value).ConfigureAwait(false)
            : await onFailure(Errors).ConfigureAwait(false);

    /// <summary>
    /// Eliminates the result by running one of two asynchronous actions; the asynchronous
    /// <see cref="Resolve(Action{TPayload}, Action{IReadOnlyList{Error}})"/>.
    /// </summary>
    /// <param name="onSuccess">Run with the success value.</param>
    /// <param name="onFailure">Run with the accumulated errors.</param>
    public async Task ResolveAsync(
        Func<TPayload, Task> onSuccess,
        Func<IReadOnlyList<Error>, Task> onFailure)
    {
        if (IsSuccess)
            await onSuccess(Value).ConfigureAwait(false);
        else
            await onFailure(Errors).ConfigureAwait(false);
    }

    /// <summary>Gets the success value without branching on <see cref="IsSuccess"/> first.</summary>
    /// <param name="value">Receives the success value, or default when this result failed.</param>
    public bool TryGetValue([MaybeNullWhen(false)] out TPayload value)
    {
        value = Value;
        return IsSuccess;
    }

    /// <summary>
    /// Compares by value: the success values must be equal and the errors equal in sequence.
    /// </summary>
    /// <param name="other">The result to compare with.</param>
    public bool Equals(Result<TPayload>? other) =>
        other is not null
        && EqualityComparer<TPayload>.Default.Equals(Value, other.Value)
        && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);

        foreach (var error in Errors)
            hash.Add(error);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Carries the errors of a result already known to be a failure into a new result of this type
    /// without copying them. Callers guarantee the list is non-empty.
    /// </summary>
    internal static Result<TPayload> Propagate(IReadOnlyList<Error> errors) => new(errors);
}

/// <summary>
/// Result is a railway-oriented outcome for an operation that produces no value, with the same
/// semantics as <see cref="Result{TPayload}"/> plus the <c>Try</c> factories that translate a
/// thrown exception into a failure. Instances are created only through the named factories.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Try</c> and <c>TryAsync</c> factories are the adapter boundary: a framework or vendor
/// exception stops there and becomes an error, so it never crosses into a domain or application
/// contract. As on <see cref="Result{TPayload}"/>, <see cref="Then(Func{Result})"/> is the monadic
/// <c>bind</c> and <see cref="Resolve{TOut}"/> is the <c>match</c> or <c>fold</c>.
/// </para>
/// <para>
/// Every catch shares one translation policy, <see cref="Error.FromCancellation"/> for an
/// <see cref="OperationCanceledException"/> and <see cref="Error.FromExceptions(Exception, string, ErrorCategory)"/>
/// for anything else. The overloads that accept a <see cref="CancellationToken"/> pass it to the
/// operation and use it as evidence: a cancellation the caller asked for is <see cref="ErrorCategory.Canceled"/>,
/// one it did not (a timeout) is <see cref="ErrorCategory.Unavailable"/>.
/// </para>
/// </remarks>
public sealed record Result
{
    private static readonly Result Succeeded = new([]);

    // Trusts its input: every caller either built the list through Error.AtLeastOne or is
    // propagating the errors of a result already known to be a failure.
    private Result(IReadOnlyList<Error> errors) => Errors = errors;

    /// <summary>Gets the errors that caused the failure, empty on success.</summary>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Completes on success, or explicitly throws with all structured errors on failure.</summary>
    /// <exception cref="ResultException">This result is a failure.</exception>
    public void ThrowIfError()
    {
        if (IsFailure) throw new ResultException(Errors);
    }

    /// <summary>Completes on success, or throws with all structured errors on failure.</summary>
    /// <exception cref="ResultException">This result is a failure.</exception>
    public void Throw() => ThrowIfError();

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => Succeeded;

    /// <summary>Creates a failed result from a single message used for both contexts.</summary>
    /// <param name="message">The message for both contexts; null becomes <see cref="Error.DefaultUserMessage"/>.</param>
    public static Result Failure(string? message) => Failure(Error.FromMessage(message));

    /// <summary>Creates a failed result from a single error.</summary>
    /// <param name="error">The error describing the failure; null is replaced by a sentinel error.</param>
    public static Result Failure(Error? error) => new(Error.AtLeastOne([error]));

    /// <summary>
    /// Creates a failed result accumulating several errors. An empty collection still yields a
    /// failure, carrying a sentinel error.
    /// </summary>
    /// <param name="errors">The errors describing the failure; null, empty, or all-null yields a sentinel error.</param>
    public static Result Failure(IEnumerable<Error?>? errors) => new(Error.AtLeastOne(errors));

    /// <summary>Runs an action that may throw, translating any exception into a failure.</summary>
    /// <param name="action">The action to run.</param>
    public static Result Try(Action action)
    {
        try
        {
            action();
            return Success();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Runs an action that may throw, translating any exception into a failure. The token is passed
    /// to the action and used to tell the caller's own cancellation from an internal timeout.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    public static Result Try(Action<CancellationToken> action, CancellationToken cancellationToken)
    {
        try
        {
            action(cancellationToken);
            return Success();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex, cancellationToken));
        }
    }

    /// <summary>Runs a function that may throw, translating any exception into a failure.</summary>
    /// <param name="func">The function to run.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static Result<T> Try<T>(Func<T> func)
    {
        try
        {
            return Result<T>.Success(func());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Runs a function that may throw, translating any exception into a failure. The token is passed
    /// to the function and used to tell the caller's own cancellation from an internal timeout.
    /// </summary>
    /// <param name="func">The function to run.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static Result<T> Try<T>(Func<CancellationToken, T> func, CancellationToken cancellationToken)
    {
        try
        {
            return Result<T>.Success(func(cancellationToken));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Propagate(Error.FromCaught(ex, cancellationToken));
        }
    }

    /// <summary>
    /// Runs an action that may throw, mapping any exception with the given handler. A cancellation
    /// is classified before the handler is consulted; a handler that returns null yields a sentinel error.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <param name="errorHandler">Translates a thrown exception into an error.</param>
    public static Result Try(Action action, Func<Exception, Error> errorHandler)
    {
        try
        {
            action();
            return Success();
        }
        catch (OperationCanceledException canceled)
        {
            return Failure(Error.FromCancellation(canceled));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failure(errorHandler(ex));
        }
    }

    /// <summary>
    /// Runs a function that may throw, mapping any exception with the given handler. A cancellation
    /// is classified before the handler is consulted; a handler that returns null yields a sentinel error.
    /// </summary>
    /// <param name="func">The function to run.</param>
    /// <param name="errorHandler">Translates a thrown exception into an error.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static Result<T> Try<T>(Func<T> func, Func<Exception, Error> errorHandler)
    {
        try
        {
            return Result<T>.Success(func());
        }
        catch (OperationCanceledException canceled)
        {
            return Result<T>.Failure(Error.FromCancellation(canceled));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Failure(errorHandler(ex));
        }
    }

    /// <summary>Runs an asynchronous action that may throw, translating any exception into a failure.</summary>
    /// <param name="action">The asynchronous action to run.</param>
    public static async Task<Result> TryAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return Success();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Runs an asynchronous action that may throw, translating any exception into a failure. The
    /// token is passed to the action and used to tell the caller's own cancellation from an
    /// internal timeout.
    /// </summary>
    /// <param name="action">The asynchronous action to run.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    public static async Task<Result> TryAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            return Success();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex, cancellationToken));
        }
    }

    /// <summary>Runs an asynchronous function that may throw, translating any exception into a failure.</summary>
    /// <param name="func">The asynchronous function to run.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func)
    {
        try
        {
            var value = await func().ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Runs an asynchronous function that may throw, translating any exception into a failure. The
    /// token is passed to the function and used to tell the caller's own cancellation from an
    /// internal timeout.
    /// </summary>
    /// <param name="func">The asynchronous function to run.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static async Task<Result<T>> TryAsync<T>(
        Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await func(cancellationToken).ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Propagate(Error.FromCaught(ex, cancellationToken));
        }
    }

    /// <summary>
    /// Runs an asynchronous action that may throw, mapping any exception with the handler. A
    /// cancellation is classified before the handler is consulted; a handler that returns null
    /// yields a sentinel error.
    /// </summary>
    /// <param name="action">The asynchronous action to run.</param>
    /// <param name="errorHandler">Translates a thrown exception into an error.</param>
    public static async Task<Result> TryAsync(Func<Task> action, Func<Exception, Error> errorHandler)
    {
        try
        {
            await action().ConfigureAwait(false);
            return Success();
        }
        catch (OperationCanceledException canceled)
        {
            return Failure(Error.FromCancellation(canceled));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failure(errorHandler(ex));
        }
    }

    /// <summary>
    /// Runs an asynchronous function that may throw, mapping any exception with the handler. A
    /// cancellation is classified before the handler is consulted; a handler that returns null
    /// yields a sentinel error.
    /// </summary>
    /// <param name="func">The asynchronous function to run.</param>
    /// <param name="errorHandler">Translates a thrown exception into an error.</param>
    /// <typeparam name="T">The payload type the function produces.</typeparam>
    public static async Task<Result<T>> TryAsync<T>(
        Func<Task<T>> func,
        Func<Exception, Error> errorHandler)
    {
        try
        {
            var value = await func().ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (OperationCanceledException canceled)
        {
            return Result<T>.Failure(Error.FromCancellation(canceled));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Failure(errorHandler(ex));
        }
    }

    /// <summary>
    /// Chains this result into the next operation, known traditionally as the monadic <c>bind</c>.
    /// </summary>
    /// <param name="nextOperation">The operation to run when this result succeeded.</param>
    public Result Then(Func<Result> nextOperation)
    {
        if (IsFailure)
            return this;

        try
        {
            return nextOperation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Chains this result into an operation that produces a value; the monadic <c>bind</c> that
    /// lifts into <see cref="Result{TPayload}"/>.
    /// </summary>
    /// <param name="nextOperation">The operation to run when this result succeeded.</param>
    /// <typeparam name="TNext">The payload type produced by the next operation.</typeparam>
    public Result<TNext> Then<TNext>(Func<Result<TNext>> nextOperation)
    {
        if (IsFailure)
            return Result<TNext>.Propagate(Errors);

        try
        {
            return nextOperation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TNext>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Chains this result into the next asynchronous operation; the asynchronous
    /// <see cref="Then(Func{Result})"/>, known traditionally as the monadic <c>bind</c>.
    /// </summary>
    /// <param name="nextOperation">The asynchronous operation to run when this result succeeded.</param>
    public async Task<Result> ThenAsync(Func<Task<Result>> nextOperation)
    {
        if (IsFailure)
            return this;

        try
        {
            return await nextOperation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Chains this result into an asynchronous operation that produces a value; the asynchronous
    /// <see cref="Then{TNext}(Func{Result{TNext}})"/>.
    /// </summary>
    /// <param name="nextOperation">The asynchronous operation to run when this result succeeded.</param>
    /// <typeparam name="TNext">The payload type produced by the next operation.</typeparam>
    public async Task<Result<TNext>> ThenAsync<TNext>(Func<Task<Result<TNext>>> nextOperation)
    {
        if (IsFailure)
            return Result<TNext>.Propagate(Errors);

        try
        {
            return await nextOperation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<TNext>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>
    /// Eliminates the result by applying one of two functions, known traditionally as <c>match</c>
    /// or <c>fold</c>.
    /// </summary>
    /// <param name="onSuccess">Applied when this result succeeded.</param>
    /// <param name="onFailure">Applied to the accumulated errors.</param>
    /// <typeparam name="TOut">The type both branches produce.</typeparam>
    public TOut Resolve<TOut>(Func<TOut> onSuccess, Func<IReadOnlyList<Error>, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Errors);

    /// <summary>
    /// Eliminates the result by running one of two actions, known traditionally as <c>match</c>.
    /// </summary>
    /// <param name="onSuccess">Run when this result succeeded.</param>
    /// <param name="onFailure">Run with the accumulated errors.</param>
    public void Resolve(Action onSuccess, Action<IReadOnlyList<Error>> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(Errors);
    }

    /// <summary>
    /// Eliminates the result by applying one of two asynchronous functions; the asynchronous
    /// <see cref="Resolve{TOut}"/>.
    /// </summary>
    /// <param name="onSuccess">Applied when this result succeeded.</param>
    /// <param name="onFailure">Applied to the accumulated errors.</param>
    /// <typeparam name="TOut">The type both branches produce.</typeparam>
    public async Task<TOut> ResolveAsync<TOut>(
        Func<Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure) =>
        IsSuccess
            ? await onSuccess().ConfigureAwait(false)
            : await onFailure(Errors).ConfigureAwait(false);

    /// <summary>
    /// Eliminates the result by running one of two asynchronous actions; the asynchronous
    /// <see cref="Resolve(Action, Action{IReadOnlyList{Error}})"/>.
    /// </summary>
    /// <param name="onSuccess">Run when this result succeeded.</param>
    /// <param name="onFailure">Run with the accumulated errors.</param>
    public async Task ResolveAsync(Func<Task> onSuccess, Func<IReadOnlyList<Error>, Task> onFailure)
    {
        if (IsSuccess)
            await onSuccess().ConfigureAwait(false);
        else
            await onFailure(Errors).ConfigureAwait(false);
    }

    /// <summary>Lifts this result into a <see cref="Result{TPayload}"/> using a value provider.</summary>
    /// <param name="valueProvider">Supplies the success value when this result succeeded.</param>
    /// <typeparam name="T">The payload type to lift into.</typeparam>
    public Result<T> ToGeneric<T>(Func<T> valueProvider)
    {
        if (IsFailure)
            return Result<T>.Propagate(Errors);

        try
        {
            return Result<T>.Success(valueProvider());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<T>.Propagate(Error.FromCaught(ex));
        }
    }

    /// <summary>Compares by value: the errors must be equal in sequence.</summary>
    /// <param name="other">The result to compare with.</param>
    public bool Equals(Result? other) =>
        other is not null && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var error in Errors)
            hash.Add(error);

        return hash.ToHashCode();
    }
}
