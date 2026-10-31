using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NyssaAI.DbGateway.Commands;

/// <summary>Implements dialect-aware parameter coercion over an ADO.NET <see cref="DbCommand"/>.</summary>
internal sealed class DbParameterBinder : IParameterBinder
{
    private readonly DbCommand _command;
    private readonly DbDialect _dialect;

    internal DbParameterBinder(DbCommand command, DbDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(command);
        _command = command;
        _dialect = dialect;
    }

    public void Add(string name, string? value)
    {
        if (value is null)
        {
            AddNull(name, DbType.String);
            return;
        }
        AddRaw(name, DbType.String, value);
    }

    public void Add(string name, long value) => AddRaw(name, DbType.Int64, value);

    public void Add(string name, long? value)
    {
        if (value is null) AddNull(name, DbType.Int64);
        else Add(name, value.Value);
    }

    public void Add(string name, int value) => AddRaw(name, DbType.Int32, value);

    public void Add(string name, int? value)
    {
        if (value is null) AddNull(name, DbType.Int32);
        else Add(name, value.Value);
    }

    public void Add(string name, double value) => AddRaw(name, DbType.Double, value);

    public void Add(string name, double? value)
    {
        if (value is null) AddNull(name, DbType.Double);
        else Add(name, value.Value);
    }

    public void Add(string name, decimal value) => AddRaw(name, DbType.Decimal, value);

    public void Add(string name, decimal? value)
    {
        if (value is null) AddNull(name, DbType.Decimal);
        else Add(name, value.Value);
    }

    public void Add(string name, bool value)
    {
        if (_dialect == DbDialect.Sqlite)
            AddRaw(name, DbType.Int64, value ? 1L : 0L);
        else
            AddRaw(name, DbType.Boolean, value);
    }

    public void Add(string name, bool? value)
    {
        if (value is null)
            AddNull(name, _dialect == DbDialect.Sqlite ? DbType.Int64 : DbType.Boolean);
        else
            Add(name, value.Value);
    }

    public void Add(string name, Guid value)
    {
        if (_dialect == DbDialect.Sqlite)
            AddRaw(name, DbType.String, value.ToString("D", CultureInfo.InvariantCulture));
        else
            AddRaw(name, DbType.Guid, value);
    }

    public void Add(string name, Guid? value)
    {
        if (value is null)
            AddNull(name, _dialect == DbDialect.Sqlite ? DbType.String : DbType.Guid);
        else
            Add(name, value.Value);
    }

    public void Add(string name, DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        if (_dialect == DbDialect.Sqlite)
            AddRaw(name, DbType.String, utc.ToString("O", CultureInfo.InvariantCulture));
        else
            AddRaw(name, DbType.DateTimeOffset, utc);
    }

    public void Add(string name, DateTimeOffset? value)
    {
        if (value is null)
            AddNull(name, _dialect == DbDialect.Sqlite ? DbType.String : DbType.DateTimeOffset);
        else
            Add(name, value.Value);
    }

    public void Add(string name, byte[]? value)
    {
        if (value is null)
        {
            AddNull(name, DbType.Binary);
            return;
        }
        AddRaw(name, DbType.Binary, value);
    }

    public void AddNull(string name, DbType dbType)
    {
        var normalized = NormalizeName(name);
        var parameter = _command.CreateParameter();
        parameter.ParameterName = normalized;
        parameter.DbType = dbType;
        parameter.Value = DBNull.Value;
        _command.Parameters.Add(parameter);
    }

    private void AddRaw(string name, DbType dbType, object value)
    {
        var normalized = NormalizeName(name);
        var parameter = _command.CreateParameter();
        parameter.ParameterName = normalized;
        parameter.DbType = dbType;
        parameter.Value = value;
        _command.Parameters.Add(parameter);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.StartsWith('@'))
        {
            if (trimmed.Length == 1)
                throw new ArgumentException("Parameter name cannot be only '@'.", nameof(name));
            return trimmed;
        }
        return "@" + trimmed;
    }
}
