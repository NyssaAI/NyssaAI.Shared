namespace NyssaAI.Results;

public sealed partial record Result
{
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
}
