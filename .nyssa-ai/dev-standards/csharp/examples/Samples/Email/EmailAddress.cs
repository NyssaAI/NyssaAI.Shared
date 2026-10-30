using Nyssa.Results;

namespace Standards.Sample.Email;

/// <summary>EmailAddress is a normalized address under this sample's bounded ASCII policy.</summary>
/// <remarks>This intentionally demonstrates a business input policy, not full RFC email validation.</remarks>
public sealed record EmailAddress
{
    /// <summary>The longest normalized address accepted by this policy.</summary>
    public const int MaxLength = 254;
    /// <summary>The validated address, preserving local-part case and normalizing its domain.</summary>
    public string Value { get; }
    private EmailAddress(string value) => Value = value;
    /// <summary>Validates before construction; malformed inputs return Validation failures.</summary>
    public static Result<EmailAddress> Create(string? value)
    {
        return Guard.Against.NullOrWhiteSpace(value, nameof(value))
            .Transform(candidate => candidate.Trim())
            .Then(candidate => Guard.Against.LengthOutOfRange(candidate, 1, MaxLength, nameof(value)))
            .Then(candidate => Guard.Against.PatternMismatch(candidate,
                @"\A[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z", nameof(value)))
            .Transform(candidate =>
            {
                var at = candidate.IndexOf('@');
                return new EmailAddress(candidate[..at] + "@" + candidate[(at + 1)..].ToLowerInvariant());
            });
    }
}
