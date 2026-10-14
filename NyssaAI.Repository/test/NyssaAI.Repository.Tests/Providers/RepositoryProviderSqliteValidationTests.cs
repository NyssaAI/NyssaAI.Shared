using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Configuration;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

/// <summary>Verifies SQLite parsing failures are sanitized results during preparation and exceptions during registration.</summary>
public sealed class RepositoryProviderSqliteValidationTests
{
    private const string DataSourceSentinel = "sentinel-sqlite-data-source.db";
    private const string PasswordSentinel = "sentinel-sqlite-password";

    /// <summary>Provider preparation returns validation for malformed Boolean and timeout values without exposing settings.</summary>
    [Theory]
    [InlineData("Pooling", "maybe")]
    [InlineData("Default Timeout", "abc")]
    [InlineData("Default Timeout", "2147483648")]
    public void Prepare_InvalidSqliteValue_ReturnsSanitizedValidation(string keyword, string value)
    {
        var result = Assert.Throws<ArgumentException>(() => RepositoryProviderSqlite.Prepare(Settings(keyword, value)));

        Assert.DoesNotContain("sentinel", result.ToString(), StringComparison.Ordinal);
    }

    /// <summary>DI registration throws validation without changing existing service descriptors or exposing settings.</summary>
    [Theory]
    [InlineData("Pooling", "maybe")]
    [InlineData("Default Timeout", "abc")]
    [InlineData("Default Timeout", "2147483648")]
    public void AddNyssaRepository_InvalidSqliteValue_ThrowsSanitizedValidationWithoutMutation(string keyword, string value)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        services.AddSingleton("existing-service");
        var originalDescriptors = services.ToArray();

        var result = Assert.Throws<ArgumentException>(() => services.AddNyssaRepository(Settings(keyword, value)));

        Assert.DoesNotContain("sentinel", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalDescriptors.Length, services.Count);
        for (var index = 0; index < originalDescriptors.Length; index++)
            Assert.Same(originalDescriptors[index], services[index]);
    }

    private static RepositoryConfiguration Settings(string keyword, string value) => new()
    {
        DatabaseProvider = "sqlite",
        ConnectionString = $"Data Source={DataSourceSentinel};Password={PasswordSentinel};{keyword}={value}"
    };

}
