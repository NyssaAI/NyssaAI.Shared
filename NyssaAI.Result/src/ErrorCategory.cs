// Classify failure outcomes without requiring callers to parse diagnostic messages.
// ErrorCategory: Unexpected | Validation | NotFound | Conflict | Forbidden | Unavailable | Canceled | Indeterminate
namespace NyssaAI.Results;

/// <summary>
/// ErrorCategory separates the kinds of failure that callers handle differently, so a handler can
/// branch on the kind without inspecting message text or retaining a framework exception.
/// </summary>
public enum ErrorCategory
{
    /// <summary>An unexpected failure; the default when a caller does not classify one.</summary>
    Unexpected = 0,

    /// <summary>Input did not satisfy a precondition or invariant.</summary>
    Validation,

    /// <summary>The requested item does not exist.</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state, such as a duplicate key.</summary>
    Conflict,

    /// <summary>The caller is not permitted to perform the operation.</summary>
    Forbidden,

    /// <summary>A transient fault that may succeed if retried, such as a timeout.</summary>
    Unavailable,

    /// <summary>
    /// The operation was cancelled before it finished.
    /// </summary>
    /// <remarks>
    /// It is distinct from <see cref="Unavailable"/> because the two call for opposite responses: a
    /// transient fault invites a retry, while a cancellation was asked for and retrying it defies
    /// the caller who asked. Reporting a cancelled run as transient is what every stage in this
    /// repository had to do before this member existed.
    /// </remarks>
    Canceled,

    /// <summary>The operation may have completed, but its authoritative outcome is unknown.</summary>
    /// <remarks>Reconcile durable state before replaying an operation that may already have taken effect.</remarks>
    Indeterminate = 7,
}
