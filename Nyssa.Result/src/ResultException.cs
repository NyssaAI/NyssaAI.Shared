namespace NyssaAI.Results;

/// <summary>ResultException carries the structured errors from an explicitly thrown failed result.</summary>
/// <remarks>Created by explicit throwing or unwrapping of a failed result. Message contains user messages, not technical details.</remarks>
public sealed class ResultException : Exception
{
    internal ResultException(IReadOnlyList<Error> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.UserMessage)))
    {
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    /// <summary>Gets an immutable snapshot of every original error, preserving order, messages, categories, and captured stack text.</summary>
    public IReadOnlyList<Error> Errors { get; }
}
