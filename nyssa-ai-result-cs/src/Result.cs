namespace NyssaAI.Results;

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
public sealed partial record Result
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
