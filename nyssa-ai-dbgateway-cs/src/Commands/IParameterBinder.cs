using System.Data;

namespace NyssaAI.DbGateway.Commands;

/// <summary>Binds typed SQL parameters with zero-reflection, dialect-aware type coercion across SQLite and PostgreSQL.</summary>
public interface IParameterBinder
{
    /// <summary>Binds a string parameter or database null when null.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The string value, or null for <see cref="DBNull.Value"/>.</param>
    public void Add(string name, string? value);

    /// <summary>Binds a 64-bit integer parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The 64-bit integer value.</param>
    public void Add(string name, long value);

    /// <summary>Binds a nullable 64-bit integer parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable 64-bit integer value.</param>
    public void Add(string name, long? value);

    /// <summary>Binds a 32-bit integer parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The 32-bit integer value.</param>
    public void Add(string name, int value);

    /// <summary>Binds a nullable 32-bit integer parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable 32-bit integer value.</param>
    public void Add(string name, int? value);

    /// <summary>Binds a double-precision floating-point parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The double value.</param>
    public void Add(string name, double value);

    /// <summary>Binds a nullable double-precision floating-point parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable double value.</param>
    public void Add(string name, double? value);

    /// <summary>Binds a decimal parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The decimal value.</param>
    public void Add(string name, decimal value);

    /// <summary>Binds a nullable decimal parameter.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable decimal value.</param>
    public void Add(string name, decimal? value);

    /// <summary>Binds a boolean parameter, coercing to <c>1</c>/<c>0</c> integer on SQLite and native <c>boolean</c> on PostgreSQL.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The boolean value.</param>
    public void Add(string name, bool value);

    /// <summary>Binds a nullable boolean parameter with dialect coercion.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable boolean value.</param>
    public void Add(string name, bool? value);

    /// <summary>Binds a GUID parameter, coercing to lowercase hyphenated <c>TEXT</c> on SQLite and native <c>uuid</c> on PostgreSQL.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The GUID value.</param>
    public void Add(string name, Guid value);

    /// <summary>Binds a nullable GUID parameter with dialect coercion.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable GUID value.</param>
    public void Add(string name, Guid? value);

    /// <summary>Binds a timestamp parameter, coercing to UTC ISO-8601 <c>TEXT</c> on SQLite and UTC <c>timestamptz</c> on PostgreSQL.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The timestamp value.</param>
    public void Add(string name, DateTimeOffset value);

    /// <summary>Binds a nullable timestamp parameter with dialect coercion.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The nullable timestamp value.</param>
    public void Add(string name, DateTimeOffset? value);

    /// <summary>Binds a binary byte array parameter or database null when null.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="value">The byte array value, or null for <see cref="DBNull.Value"/>.</param>
    public void Add(string name, byte[]? value);

    /// <summary>Binds an explicit database null with the specified <see cref="DbType"/>.</summary>
    /// <param name="name">The parameter name, with or without a leading <c>@</c>.</param>
    /// <param name="dbType">The target ADO.NET database type.</param>
    public void AddNull(string name, DbType dbType);
}
