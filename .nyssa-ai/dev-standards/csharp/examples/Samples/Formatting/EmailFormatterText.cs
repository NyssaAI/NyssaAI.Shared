using Standards.Sample.Email;

namespace Standards.Sample.Formatting;

/// <summary>EmailFormatterText produces a human-readable email representation.</summary>
public sealed class EmailFormatterText : IEmailFormatter
{
    /// <inheritdoc />
    public string Format(EmailAddress value) => "email: " + value.Value;
}
