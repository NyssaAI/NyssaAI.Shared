using System.Text.Encodings.Web;
using System.Text.Json;
using Standards.Sample.Email;

namespace Standards.Sample.Formatting;

/// <summary>EmailFormatterJson produces a JSON representation with one email field.</summary>
public sealed class EmailFormatterJson : IEmailFormatter
{
    /// <inheritdoc />
    public string Format(EmailAddress value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("email", value.Value);
            writer.WriteEndObject();
        }
        // This is a standalone JSON payload, never HTML. Values obey the bounded ASCII policy.
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
