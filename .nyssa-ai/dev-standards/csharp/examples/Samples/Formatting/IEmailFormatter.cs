using Standards.Sample.Email;

namespace Standards.Sample.Formatting;

/// <summary>IEmailFormatter renders a validated address without mutation or I/O.</summary>
/// <remarks>Output includes the exact normalized address. Representation varies by provider.</remarks>
public interface IEmailFormatter
{
    /// <summary>Renders the supplied valid address. Null is outside this non-null contract.</summary>
    string Format(EmailAddress value);
}
