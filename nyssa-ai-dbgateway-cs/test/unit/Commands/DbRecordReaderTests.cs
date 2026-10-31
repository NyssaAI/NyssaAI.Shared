using Microsoft.Data.Sqlite;
using NyssaAI.DbGateway.Commands;
using Xunit;

namespace NyssaAI.DbGateway.Tests.Commands;

public sealed class DbRecordReaderTests
{
    [Fact]
    public async Task GetTypedValues_SqliteRepresentations_CoercesGuidBoolDateTimeOffsetAndNumbers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var expectedGuid = Guid.Parse("a8098c1a-f86e-11da-bd1a-00112444be1e");
        var guidBytes = expectedGuid.ToByteArray();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                'a8098c1a-f86e-11da-bd1a-00112444be1e',
                @guidBlob,
                1,
                0,
                '2026-10-31T20:30:00.0000000+00:00',
                1793478600,
                42,
                123.45,
                NULL
            """;
        var blobParam = command.CreateParameter();
        blobParam.ParameterName = "@guidBlob";
        blobParam.Value = guidBytes;
        command.Parameters.Add(blobParam);

        await using var dbReader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var reader = new DbRecordReader(dbReader);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(9, reader.FieldCount);
        Assert.Equal(expectedGuid, reader.GetGuid(0));
        Assert.Equal(expectedGuid, reader.GetGuid(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero), reader.GetDateTimeOffset(4));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793478600), reader.GetDateTimeOffset(5));
        Assert.Equal(42, reader.GetInt32(6));
        Assert.Equal(42L, reader.GetInt64(6));
        Assert.Equal(123.45m, reader.GetDecimal(7));
        Assert.True(reader.IsDBNull(8));
        Assert.Null(reader.GetNullableString(8));
        Assert.Null(reader.GetNullableGuid(8));
        Assert.Null(reader.GetNullableBoolean(8));
        Assert.Null(reader.GetNullableDateTimeOffset(8));
        Assert.Null(reader.GetNullableInt32(8));
        Assert.Null(reader.GetNullableInt64(8));
    }
}
