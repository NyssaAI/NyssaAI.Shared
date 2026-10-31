namespace NyssaAI.DbGateway.Commands;

/// <summary>Provides dialect-normalized, reflection-free reading of columns from an active database result set.</summary>
public interface IDataRecordReader
{
    /// <summary>Gets the number of columns in the current row.</summary>
    public int FieldCount { get; }

    /// <summary>Advances the reader to the next record in the result set.</summary>
    /// <param name="cancellationToken">Cancels asynchronous reading.</param>
    /// <returns><c>true</c> if another row is available; otherwise <c>false</c>.</returns>
    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks whether the specified column contains database null.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns><c>true</c> if the column is <see cref="DBNull"/>; otherwise <c>false</c>.</returns>
    public bool IsDBNull(int ordinal);

    /// <summary>Reads a string column value.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The non-null string value.</returns>
    public string GetString(int ordinal);

    /// <summary>Reads a nullable string column value.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The string value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public string? GetNullableString(int ordinal);

    /// <summary>Reads a 64-bit integer column, coercing smaller integer storage types automatically.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The 64-bit integer value.</returns>
    public long GetInt64(int ordinal);

    /// <summary>Reads a nullable 64-bit integer column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The 64-bit integer value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public long? GetNullableInt64(int ordinal);

    /// <summary>Reads a 32-bit integer column, coercing from 64-bit integer storage on SQLite when within range.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The 32-bit integer value.</returns>
    public int GetInt32(int ordinal);

    /// <summary>Reads a nullable 32-bit integer column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The 32-bit integer value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public int? GetNullableInt32(int ordinal);

    /// <summary>Reads a double-precision floating-point column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The double value.</returns>
    public double GetDouble(int ordinal);

    /// <summary>Reads a nullable double-precision floating-point column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The double value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public double? GetNullableDouble(int ordinal);

    /// <summary>Reads a decimal column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The decimal value.</returns>
    public decimal GetDecimal(int ordinal);

    /// <summary>Reads a nullable decimal column.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The decimal value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public decimal? GetNullableDecimal(int ordinal);

    /// <summary>Reads a boolean column, coercing <c>0</c>/<c>1</c> integers on SQLite and native booleans on PostgreSQL.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The boolean value.</returns>
    public bool GetBoolean(int ordinal);

    /// <summary>Reads a nullable boolean column with dialect coercion.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The boolean value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public bool? GetNullableBoolean(int ordinal);

    /// <summary>Reads a GUID column, coercing <c>TEXT</c> or 16-byte <c>BLOB</c> on SQLite and native <c>uuid</c> on PostgreSQL.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The GUID value.</returns>
    public Guid GetGuid(int ordinal);

    /// <summary>Reads a nullable GUID column with dialect coercion.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The GUID value, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public Guid? GetNullableGuid(int ordinal);

    /// <summary>Reads a UTC timestamp column, coercing ISO-8601 <c>TEXT</c> or Unix epoch seconds/milliseconds on SQLite and <c>timestamptz</c> on PostgreSQL.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The timestamp normalized to UTC.</returns>
    public DateTimeOffset GetDateTimeOffset(int ordinal);

    /// <summary>Reads a nullable UTC timestamp column with dialect coercion.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The timestamp normalized to UTC, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public DateTimeOffset? GetNullableDateTimeOffset(int ordinal);

    /// <summary>Reads a binary column as a byte array.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The byte array.</returns>
    public byte[] GetBytes(int ordinal);

    /// <summary>Reads a nullable binary column as a byte array.</summary>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The byte array, or <c>null</c> if the column is <see cref="DBNull"/>.</returns>
    public byte[]? GetNullableBytes(int ordinal);
}
