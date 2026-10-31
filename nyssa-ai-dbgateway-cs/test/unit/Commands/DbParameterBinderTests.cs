using System.Data;
using Microsoft.Data.Sqlite;
using Npgsql;
using NyssaAI.DbGateway.Commands;
using Xunit;

namespace NyssaAI.DbGateway.Tests.Commands;

public sealed class DbParameterBinderTests
{
    [Fact]
    public void Add_SqliteDialect_CoercesGuidBoolAndDateTimeOffsetToSqliteStorageTypes()
    {
        using var command = new SqliteCommand();
        var binder = new DbParameterBinder(command, DbDialect.Sqlite);
        var id = Guid.Parse("d4a7e904-730b-4f88-9a21-2258a9976419");
        var timestamp = new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.FromHours(-5));

        binder.Add("id", id);
        binder.Add("@active", true);
        binder.Add("inactive", false);
        binder.Add("@createdAt", timestamp);

        Assert.Equal(4, command.Parameters.Count);
        Assert.Equal("@id", command.Parameters[0].ParameterName);
        Assert.Equal(DbType.String, command.Parameters[0].DbType);
        Assert.Equal("d4a7e904-730b-4f88-9a21-2258a9976419", command.Parameters[0].Value);

        Assert.Equal("@active", command.Parameters[1].ParameterName);
        Assert.Equal(DbType.Int64, command.Parameters[1].DbType);
        Assert.Equal(1L, command.Parameters[1].Value);

        Assert.Equal("@inactive", command.Parameters[2].ParameterName);
        Assert.Equal(DbType.Int64, command.Parameters[2].DbType);
        Assert.Equal(0L, command.Parameters[2].Value);

        Assert.Equal("@createdAt", command.Parameters[3].ParameterName);
        Assert.Equal(DbType.String, command.Parameters[3].DbType);
        Assert.Equal("2026-10-31T20:30:00.0000000+00:00", command.Parameters[3].Value);
    }

    [Fact]
    public void Add_PostgreSqlDialect_PreservesNativeGuidBoolAndUtcDateTimeOffset()
    {
        using var command = new NpgsqlCommand();
        var binder = new DbParameterBinder(command, DbDialect.PostgreSql);
        var id = Guid.Parse("d4a7e904-730b-4f88-9a21-2258a9976419");
        var timestamp = new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.FromHours(-5));

        binder.Add("id", id);
        binder.Add("active", true);
        binder.Add("createdAt", timestamp);

        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal(DbType.Guid, command.Parameters[0].DbType);
        Assert.Equal(id, command.Parameters[0].Value);

        Assert.Equal(DbType.Boolean, command.Parameters[1].DbType);
        Assert.Equal(true, command.Parameters[1].Value);

        Assert.Equal(DbType.DateTimeOffset, command.Parameters[2].DbType);
        var boundDto = Assert.IsType<DateTimeOffset>(command.Parameters[2].Value);
        Assert.Equal(TimeSpan.Zero, boundDto.Offset);
        Assert.Equal(timestamp.ToUniversalTime(), boundDto);
    }

    [Fact]
    public void Add_NullAndNumericValues_BindsExpectedDbTypesAndDBNull()
    {
        using var command = new SqliteCommand();
        var binder = new DbParameterBinder(command, DbDialect.Sqlite);

        binder.Add("nullText", (string?)null);
        binder.Add("nullGuid", (Guid?)null);
        binder.Add("nullBool", (bool?)null);
        binder.Add("nullTime", (DateTimeOffset?)null);
        binder.Add("count64", 42L);
        binder.Add("count32", 7);
        binder.Add("ratio", 3.5d);
        binder.Add("amount", 19.95m);
        binder.Add("payload", new byte[] { 1, 2, 3 });

        Assert.Equal(DBNull.Value, command.Parameters[0].Value);
        Assert.Equal(DBNull.Value, command.Parameters[1].Value);
        Assert.Equal(DBNull.Value, command.Parameters[2].Value);
        Assert.Equal(DBNull.Value, command.Parameters[3].Value);
        Assert.Equal(42L, command.Parameters[4].Value);
        Assert.Equal(7, command.Parameters[5].Value);
        Assert.Equal(3.5d, command.Parameters[6].Value);
        Assert.Equal(19.95m, command.Parameters[7].Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, command.Parameters[8].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    public void Add_InvalidParameterName_ThrowsArgumentException(string invalidName)
    {
        using var command = new SqliteCommand();
        var binder = new DbParameterBinder(command, DbDialect.Sqlite);

        Assert.Throws<ArgumentException>(() => binder.Add(invalidName, "value"));
    }
}
