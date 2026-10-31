using System.Text.Json;

namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// Serializes a <see cref="LogRecord"/> to a single UTF-8 JSON line using <see cref="Utf8JsonWriter"/>
/// without reflection so file logging stays safe under trimming and Native AOT.
/// </summary>
internal static class LogRecordSerializer
{
    /// <summary>
    /// Serialize a record to one UTF-8 JSON line. Written field by field with
    /// Utf8JsonWriter rather than JsonSerializer, because reflection-based serialization is
    /// not safe under trimming or native AOT.
    /// </summary>
    public static byte[] Serialize(LogRecord record)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("Timestamp", record.Timestamp);
            json.WriteString("Level", record.Level);
            json.WriteString("Message", record.Message);
            json.WriteString("Module", record.Module);
            json.WriteString("Operation", record.Operation);

            json.WriteString("Exception", record.Exception);

            json.WriteStartObject("AdditionalFields");
            foreach (var field in record.AdditionalFields)
            {
                WriteField(json, field.Key, field.Value);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Write one structured field. Values arrive as object from the logging state, so each
    /// supported type is matched explicitly; anything else is written as text.
    /// </summary>
    private static void WriteField(Utf8JsonWriter json, string name, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNull(name);
                break;
            case string text:
                json.WriteString(name, text);
                break;
            case bool flag:
                json.WriteBoolean(name, flag);
                break;
            case int number:
                json.WriteNumber(name, number);
                break;
            case long number:
                json.WriteNumber(name, number);
                break;
            case double number:
                json.WriteNumber(name, number);
                break;
            case float number:
                json.WriteNumber(name, number);
                break;
            case decimal number:
                json.WriteNumber(name, number);
                break;
            case DateTime timestamp:
                json.WriteString(name, timestamp);
                break;
            case DateTimeOffset timestamp:
                json.WriteString(name, timestamp);
                break;
            case Guid id:
                json.WriteString(name, id);
                break;
            default:
                json.WriteString(name, value.ToString() ?? string.Empty);
                break;
        }
    }
}
