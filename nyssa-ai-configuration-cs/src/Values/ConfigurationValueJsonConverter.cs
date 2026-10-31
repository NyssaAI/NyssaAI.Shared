using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Contracts;

/// <summary>Preserves JSON value types and provider metadata while reading legacy string values.</summary>
internal sealed class ConfigurationValueJsonConverter : JsonConverter<ConfigurationValue>
{
    /// <inheritdoc/>
    public override ConfigurationValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return ReadValue(ref reader);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException("The configuration entry contains an invalid field type.", exception);
        }
    }

    private static ConfigurationValue ReadValue(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("value", out var value))
            throw new JsonException("The configuration entry requires a value.");
        var provider = root.TryGetProperty("provider", out var providerValue) ? providerValue.GetString() ?? "" : "";
        var encrypted = root.TryGetProperty("encrypted", out var encryptedValue) && encryptedValue.GetBoolean();
        var isObject = value.ValueKind != JsonValueKind.String;
        var isJson = isObject || (root.TryGetProperty("json", out var json) && json.GetBoolean());
        if (isObject && (encrypted || provider.Length != 0))
            throw new JsonException("Structured configuration values must use plaintext storage.");
        return new ConfigurationValue
        {
            Value = isJson && provider.Length == 0 ? value.GetRawText() : value.GetString()!,
            IsJsonValue = isJson,
            Provider = provider,
            Encrypted = encrypted,
            ReauthRequired = root.TryGetProperty("reauth_required", out var reauth) && reauth.GetBoolean(),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ConfigurationValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("value");
        if (value.IsJsonValue && value.IsLiteral && !value.Encrypted)
        {
            using var document = JsonDocument.Parse(value.Value);
            document.RootElement.WriteTo(writer);
        }
        else
            writer.WriteStringValue(value.Value);
        if (value.Encrypted)
            writer.WriteBoolean("encrypted", true);
        if (!value.IsLiteral)
            writer.WriteString("provider", value.Provider);
        if (value.ReauthRequired)
            writer.WriteBoolean("reauth_required", true);
        if (value.IsJsonValue && (!value.IsLiteral || value.Value.TrimStart().StartsWith('"')))
            writer.WriteBoolean("json", true);
        writer.WriteEndObject();
    }
}
