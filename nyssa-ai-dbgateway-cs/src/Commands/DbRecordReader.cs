using System.Data.Common;
using System.Globalization;

namespace NyssaAI.DbGateway.Commands;

/// <summary>Wraps a <see cref="DbDataReader"/> with dialect-consistent type coercion across SQLite and PostgreSQL.</summary>
internal sealed class DbRecordReader : IDataRecordReader
{
    private readonly DbDataReader _reader;

    internal DbRecordReader(DbDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public int FieldCount => _reader.FieldCount;

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default) =>
        new(_reader.ReadAsync(cancellationToken));

    public bool IsDBNull(int ordinal) => _reader.IsDBNull(ordinal);

    public string GetString(int ordinal) => _reader.GetString(ordinal);

    public string? GetNullableString(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);

    public long GetInt64(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            long value => value,
            int value => value,
            short value => value,
            byte value => value,
            sbyte value => value,
            _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture)
        };
    }

    public long? GetNullableInt64(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetInt64(ordinal);

    public int GetInt32(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            int value => value,
            long value => checked((int)value),
            short value => value,
            byte value => value,
            sbyte value => value,
            _ => Convert.ToInt32(raw, CultureInfo.InvariantCulture)
        };
    }

    public int? GetNullableInt32(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetInt32(ordinal);

    public double GetDouble(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            double value => value,
            float value => value,
            decimal value => (double)value,
            long value => value,
            int value => value,
            _ => Convert.ToDouble(raw, CultureInfo.InvariantCulture)
        };
    }

    public double? GetNullableDouble(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetDouble(ordinal);

    public decimal GetDecimal(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            decimal value => value,
            double value => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            float value => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            long value => value,
            int value => value,
            string text => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
        };
    }

    public decimal? GetNullableDecimal(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetDecimal(ordinal);

    public bool GetBoolean(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            bool value => value,
            long value => value != 0L,
            int value => value != 0,
            short value => value != 0,
            byte value => value != 0,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt) => parsedInt != 0L,
            _ => throw new InvalidCastException($"Column {ordinal} cannot be coerced to Boolean.")
        };
    }

    public bool? GetNullableBoolean(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetBoolean(ordinal);

    public Guid GetGuid(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            Guid value => value,
            string text => Guid.Parse(text, CultureInfo.InvariantCulture),
            byte[] { Length: 16 } bytes => new Guid(bytes),
            _ => throw new InvalidCastException($"Column {ordinal} cannot be coerced to Guid.")
        };
    }

    public Guid? GetNullableGuid(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetGuid(ordinal);

    public DateTimeOffset GetDateTimeOffset(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        return raw switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime()),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            long unixSeconds => DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
            _ => throw new InvalidCastException($"Column {ordinal} cannot be coerced to DateTimeOffset.")
        };
    }

    public DateTimeOffset? GetNullableDateTimeOffset(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetDateTimeOffset(ordinal);

    public byte[] GetBytes(int ordinal)
    {
        var raw = _reader.GetValue(ordinal);
        if (raw is byte[] bytes)
            return bytes;
        throw new InvalidCastException($"Column {ordinal} is not a byte array.");
    }

    public byte[]? GetNullableBytes(int ordinal) => _reader.IsDBNull(ordinal) ? null : GetBytes(ordinal);
}
