using System.Text.Json;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.Tests.Configuration;

public sealed class RepositoryConfigurationTests
{
    [Theory]
    [InlineData("sqlite", " Data Source=tasks.db;Password=sentinel-secret; ")]
    [InlineData("postgresql", "Host=localhost;Password=sentinel-secret")]
    public void Serialize_GeneratedMetadata_RoundTripsSettings(string provider, string connectionString)
    {
        var settings = new RepositoryConfiguration
        {
            DatabaseProvider = provider,
            ConnectionString = connectionString
        };

        var json = JsonSerializer.Serialize(settings, RepositoryConfigurationJsonContext.Default.RepositoryConfiguration);
        var restored = JsonSerializer.Deserialize(json, RepositoryConfigurationJsonContext.Default.RepositoryConfiguration);

        Assert.NotNull(restored);
        Assert.Equal(settings, restored);
        Assert.Equal(provider, restored.DatabaseProvider);
        Assert.Equal(connectionString, restored.ConnectionString);
        Assert.True(restored.Validate().IsSuccess);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"DatabaseProvider\":\"sqlite\"}")]
    [InlineData("{\"ConnectionString\":\"Data Source=tasks.db\"}")]
    public void Deserialize_MissingRequiredProperty_RejectsPayload(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, RepositoryConfigurationJsonContext.Default.RepositoryConfiguration));
    }

    [Fact]
    public void Deserialize_NullPayload_ReturnsNoSettingsForCompositionToReject()
    {
        var settings = JsonSerializer.Deserialize("null", RepositoryConfigurationJsonContext.Default.RepositoryConfiguration);

        Assert.Null(settings);
    }

    [Theory]
    [InlineData("{\"DatabaseProvider\":null,\"ConnectionString\":\"Password=sentinel-secret\"}")]
    [InlineData("{\"DatabaseProvider\":\"\",\"ConnectionString\":\"Password=sentinel-secret\"}")]
    [InlineData("{\"DatabaseProvider\":\" \\t\\r\\n\",\"ConnectionString\":\"Password=sentinel-secret\"}")]
    [InlineData("{\"DatabaseProvider\":\"sentinel-secret\",\"ConnectionString\":null}")]
    [InlineData("{\"DatabaseProvider\":\"sentinel-secret\",\"ConnectionString\":\"\"}")]
    [InlineData("{\"DatabaseProvider\":\"sentinel-secret\",\"ConnectionString\":\" \\t\\r\\n\"}")]
    [InlineData("{\"DatabaseProvider\":null,\"ConnectionString\":null}")]
    public void Validate_NullOrBlankField_ReturnsSanitizedValidationFailure(string json)
    {
        var settings = JsonSerializer.Deserialize(json, RepositoryConfigurationJsonContext.Default.RepositoryConfiguration);
        Assert.NotNull(settings);

        var result = settings.Validate();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" SQLITE ", "sqlite")]
    [InlineData("PostgreSQL", "postgresql")]
    [InlineData(" CUSTOM-DRIVER ", "custom-driver")]
    public void DatabaseProvider_MixedCaseOrPadding_NormalizesOnlyProvider(string provider, string expected)
    {
        const string connectionString = " Password=CaseSensitive; ";
        var settings = new RepositoryConfiguration
        {
            DatabaseProvider = provider,
            ConnectionString = connectionString
        };

        Assert.Equal(expected, settings.DatabaseProvider);
        Assert.Equal(connectionString, settings.ConnectionString);
    }

    [Fact]
    public void Validate_UnknownProviderAndUnparsedConnection_DefersCompositionChecks()
    {
        var settings = new RepositoryConfiguration
        {
            DatabaseProvider = "custom-driver",
            ConnectionString = "deliberately not a connection string"
        };

        Assert.True(settings.Validate().IsSuccess);
    }

    [Fact]
    public void ToString_SettingsAndRecordCopy_ExposeNoFieldValues()
    {
        var settings = new RepositoryConfiguration
        {
            DatabaseProvider = "provider-sentinel",
            ConnectionString = "Password=sentinel-secret"
        };
        var copy = settings with { ConnectionString = "Password=copy-secret" };

        Assert.Equal(nameof(RepositoryConfiguration), settings.ToString());
        Assert.Equal(nameof(RepositoryConfiguration), $"{settings}");
        Assert.Equal(nameof(RepositoryConfiguration), copy.ToString());
    }
}
