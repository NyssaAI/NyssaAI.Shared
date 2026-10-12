// Represent sanitized or captured failure information as an immutable application value.
// Error
// +-- UserMessage, TechnicalMessage, Category, StackTrace
// +-- FromMessage, FromMessages, FromException, FromExceptions
// +-- Canceled, FromCancellation
// +-- DefaultUserMessage, CanceledUserMessage, TimedOutUserMessage
using System.Text;

namespace NyssaAI.Results;

/// <summary>
/// Error is an immutable description of a single failure, carrying a user-safe message, a
/// technical message, and the stack trace of the failure when one was captured. Instances are
/// created only through the named factories, so every error records both messages.
/// </summary>
/// <remarks>
/// <para>
/// An error created from a caught exception copies out everything it needs at construction: the
/// message chain into <see cref="TechnicalMessage"/> and the frames of the whole inner chain into
/// <see cref="StackTrace"/>. The exception is not retained, so an error stays a serializable value
/// and no framework or vendor exception crosses into a domain or application contract.
/// </para>
/// <para>
/// No factory throws. A null message falls back to <see cref="DefaultUserMessage"/> (or, for the
/// technical message, to the user message), an undefined <see cref="ErrorCategory"/> becomes
/// <see cref="ErrorCategory.Unexpected"/>, and a null exception yields a sentinel error that says
/// no exception detail was supplied.
/// </para>
/// </remarks>
public sealed record Error
{
    /// <summary>The user message applied when a caller supplies an exception but no user message.</summary>
    public const string DefaultUserMessage = "An unexpected error occurred";

    /// <summary>The user message every cancellation error carries.</summary>
    public const string CanceledUserMessage = "The operation was cancelled.";

    /// <summary>The user message a timeout mistaken for a cancellation carries.</summary>
    public const string TimedOutUserMessage = "The operation timed out.";

    private const string MissingExceptionDetail = "A failure was reported without any exception detail";
    private const string MissingErrorDetail = "A failure was reported without any error";

    private Error(
        string? userMessage,
        string? technicalMessage,
        string? stackTrace,
        ErrorCategory category)
    {
        UserMessage = userMessage ?? DefaultUserMessage;
        TechnicalMessage = technicalMessage ?? UserMessage;
        StackTrace = stackTrace;
        Category = category is >= ErrorCategory.Unexpected and <= ErrorCategory.Indeterminate
            ? category
            : ErrorCategory.Unexpected;
    }

    /// <summary>Gets the message safe to display to end users.</summary>
    public string UserMessage { get; }

    /// <summary>Gets the detailed message for diagnostics and logging.</summary>
    public string TechnicalMessage { get; }

    /// <summary>
    /// Gets the kind of failure, so a handler can branch on it without matching message text.
    /// </summary>
    public ErrorCategory Category { get; }

    /// <summary>
    /// Gets the stack trace of the failure, or null when none was captured. The runtime captures a
    /// stack trace at throw, so an exception that was constructed but never thrown carries none.
    /// </summary>
    public string? StackTrace { get; }

    /// <summary>Creates an error whose single message serves both the user and technical contexts.</summary>
    /// <param name="message">The message to use for both contexts; null becomes <see cref="DefaultUserMessage"/>.</param>
    /// <param name="category">The kind of failure this error represents.</param>
    public static Error FromMessage(
        string? message,
        ErrorCategory category = ErrorCategory.Unexpected) =>
        new(message, message, stackTrace: null, category);

    /// <summary>Creates an error with separate user-facing and technical messages.</summary>
    /// <param name="userMessage">Message safe to display to end users; null becomes <see cref="DefaultUserMessage"/>.</param>
    /// <param name="technicalMessage">Detailed message for diagnostics and logging; null falls back to the user message.</param>
    /// <param name="category">The kind of failure this error represents.</param>
    public static Error FromMessages(
        string? userMessage,
        string? technicalMessage,
        ErrorCategory category = ErrorCategory.Unexpected) =>
        new(userMessage, technicalMessage, stackTrace: null, category);

    /// <summary>
    /// Creates an error from a caught exception, deriving the technical message from the exception
    /// type, message, and inner chain, and capturing the stack trace. An
    /// <see cref="AggregateException"/> is described whole; use
    /// <see cref="FromExceptions(Exception, string, ErrorCategory)"/> to expand one into separate errors.
    /// A null exception yields a sentinel error rather than a throw.
    /// </summary>
    /// <param name="exception">The caught exception to convert; null yields the sentinel error.</param>
    /// <param name="userMessage">Message safe to display to end users; null becomes <see cref="DefaultUserMessage"/>.</param>
    /// <param name="category">The kind of failure this error represents.</param>
    public static Error FromException(
        Exception? exception,
        string? userMessage = DefaultUserMessage,
        ErrorCategory category = ErrorCategory.Unexpected)
    {
        if (exception is null)
            return new Error(userMessage, MissingExceptionDetail, stackTrace: null, category);

        return new Error(
            userMessage,
            Describe(exception),
            CaptureStackTrace(exception),
            category);
    }

    /// <summary>
    /// Creates one error per underlying failure. An <see cref="AggregateException"/> is flattened
    /// so each inner exception is expanded. A ResultException preserves its original errors, ignoring
    /// the replacement message and category; other exceptions yield a single error, and
    /// a null exception yields the sentinel error rather than an empty list.
    /// </summary>
    /// <param name="exception">The caught exception to expand; null yields the sentinel error.</param>
    /// <param name="userMessage">Message safe to display to end users; null becomes <see cref="DefaultUserMessage"/>.</param>
    /// <param name="category">The kind of failure these errors represent.</param>
    public static IReadOnlyList<Error> FromExceptions(
        Exception? exception,
        string? userMessage = DefaultUserMessage,
        ErrorCategory category = ErrorCategory.Unexpected)
    {
        if (exception is ResultException resultException)
            return resultException.Errors;

        if (exception is not AggregateException aggregate)
            return [FromException(exception, userMessage, category)];

        var inner = aggregate.Flatten().InnerExceptions;

        // An empty result would make the enclosing Result report success, so the aggregate itself
        // stands in as the one error when it carries no inner exceptions.
        if (inner.Count == 0)
            return [FromException(aggregate, userMessage, category)];

        var errors = new List<Error>(inner.Count);
        foreach (var nested in inner)
        {
            if (nested is ResultException nestedResult)
                errors.AddRange(nestedResult.Errors);
            else
                errors.Add(FromException(nested, userMessage, category));
        }

        return errors.AsReadOnly();
    }

    /// <summary>
    /// Creates one error per underlying failure across a sequence of exceptions, flattening any
    /// <see cref="AggregateException"/> encountered. A null or empty sequence yields one sentinel
    /// error, so the enclosing result still reports a failure.
    /// </summary>
    /// <param name="exceptions">The caught exceptions to expand; null or empty yields the sentinel error.</param>
    /// <param name="userMessage">Message safe to display to end users; null becomes <see cref="DefaultUserMessage"/>.</param>
    /// <param name="category">The kind of failure these errors represent.</param>
    public static IReadOnlyList<Error> FromExceptions(
        IEnumerable<Exception>? exceptions,
        string? userMessage = DefaultUserMessage,
        ErrorCategory category = ErrorCategory.Unexpected)
    {
        var errors = new List<Error>();

        if (exceptions is not null)
        {
            foreach (var exception in exceptions)
                errors.AddRange(FromExceptions(exception, userMessage, category));
        }

        // See FromExceptions(Exception, string, ErrorCategory): a failure must never carry an empty error list.
        if (errors.Count == 0)
            errors.Add(new Error(userMessage, MissingExceptionDetail, stackTrace: null, category));

        return errors.AsReadOnly();
    }

    /// <summary>
    /// The error a cancelled operation reports. It carries <see cref="ErrorCategory.Canceled"/>
    /// rather than <see cref="ErrorCategory.Unavailable"/>, because a cancellation was asked for and
    /// retrying it defies the caller who asked. Use this when the caller's own token is known to
    /// have fired; use <see cref="FromCancellation"/> when the exception's origin is uncertain.
    /// </summary>
    /// <param name="canceled">
    /// The cancellation that was observed. Null still reports <see cref="ErrorCategory.Canceled"/>,
    /// with a technical message saying no exception detail was supplied.
    /// </param>
    public static Error Canceled(OperationCanceledException? canceled) => new(
        CanceledUserMessage,
        $"The operation was cancelled before it finished: {canceled?.GetType().Name ?? MissingExceptionDetail}",
        canceled is null ? null : CaptureStackTrace(canceled),
        ErrorCategory.Canceled);

    /// <summary>
    /// Classifies an <see cref="OperationCanceledException"/> by evidence rather than by type. A
    /// <see cref="TaskCanceledException"/> is also what <c>HttpClient</c> throws on a timeout and
    /// what a linked timeout source produces, and neither was asked for by the caller.
    /// </summary>
    /// <remarks>
    /// When <paramref name="callerToken"/> is supplied, the error is <see cref="ErrorCategory.Canceled"/>
    /// only when that token has been cancelled or the exception carries it; any other cancellation
    /// (an internal timeout, a linked source the caller never saw) is <see cref="ErrorCategory.Unavailable"/>.
    /// Without a caller token, an inner <see cref="TimeoutException"/> or an exception whose own
    /// token was never cancelled is <see cref="ErrorCategory.Unavailable"/>; a cancellation whose
    /// token did fire is <see cref="ErrorCategory.Canceled"/>.
    /// </remarks>
    /// <param name="canceled">The cancellation that was observed; null yields the <see cref="Canceled"/> sentinel.</param>
    /// <param name="callerToken">The token the caller passed in, when one is in hand.</param>
    public static Error FromCancellation(
        OperationCanceledException? canceled,
        CancellationToken? callerToken = null)
    {
        if (canceled is null)
            return Canceled(canceled);

        var askedFor = callerToken is { } token
            ? token.IsCancellationRequested || (token.CanBeCanceled && canceled.CancellationToken == token)
            : canceled.InnerException is not TimeoutException && canceled.CancellationToken.IsCancellationRequested;

        return askedFor
            ? Canceled(canceled)
            : FromException(canceled, TimedOutUserMessage, ErrorCategory.Unavailable);
    }

    /// <summary>
    /// Translates any caught exception under the single policy every <c>Try</c> and composition
    /// operator shares: a cancellation is classified with <see cref="FromCancellation"/>, anything
    /// else with <see cref="FromExceptions(Exception, string, ErrorCategory)"/>.
    /// </summary>
    internal static IReadOnlyList<Error> FromCaught(Exception? exception, CancellationToken? callerToken = null) =>
        exception is OperationCanceledException canceled
            ? [FromCancellation(canceled, callerToken)]
            : FromExceptions(exception);

    /// <summary>
    /// Copies a caller's error sequence into a list that a failure can carry: null entries are
    /// dropped, and a null or empty sequence becomes one sentinel error so the failure is never
    /// mistaken for a success.
    /// </summary>
    internal static IReadOnlyList<Error> AtLeastOne(IEnumerable<Error?>? errors)
    {
        List<Error>? collected = null;

        if (errors is not null)
        {
            foreach (var error in errors)
            {
                if (error is not null)
                    (collected ??= []).Add(error);
            }
        }

        return collected is null
            ? [new Error(DefaultUserMessage, MissingErrorDetail, stackTrace: null, ErrorCategory.Unexpected)]
            : collected.AsReadOnly();
    }

    // Prints whether a stack trace was captured rather than its full text, so ToString stays a
    // single log line. On a sealed record with no base record this member is private, not an
    // override. Read StackTrace directly for the trace itself.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserMessage = ").Append(UserMessage);
        builder.Append(", TechnicalMessage = ").Append(TechnicalMessage);

        if (Category is not ErrorCategory.Unexpected)
            builder.Append(", Category = ").Append(Category);

        if (StackTrace is not null)
            builder.Append(", StackTrace = (captured)");

        return true;
    }

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
                builder.Append(" ---> ");

            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Copies the frames of an exception and of every inner exception. The exception is discarded
    /// once the error is built, so anything not copied here is lost.
    /// </summary>
    private static string? CaptureStackTrace(Exception exception)
    {
        StringBuilder? builder = null;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.StackTrace is null)
                continue;

            if (builder is null)
            {
                builder = new StringBuilder(current.StackTrace);
                continue;
            }

            builder.AppendLine()
                .Append("--- inner exception: ")
                .Append(current.GetType().Name)
                .AppendLine(" ---")
                .Append(current.StackTrace);
        }

        return builder?.ToString();
    }
}
