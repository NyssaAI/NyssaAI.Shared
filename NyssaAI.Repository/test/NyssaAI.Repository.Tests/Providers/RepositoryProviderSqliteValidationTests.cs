using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;
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
        var result = RepositoryProviderSqlite.Prepare(Settings(keyword, value));

        Assert.True(result.IsFailure);
        AssertSanitizedValidation(Assert.Single(result.Errors), value);
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

        var result = Assert.Throws<ResultException>(() => services.AddNyssaRepository(Settings(keyword, value)));

        AssertSanitizedValidation(Assert.Single(result.Errors), value);
        Assert.Equal(originalDescriptors.Length, services.Count);
        for (var index = 0; index < originalDescriptors.Length; index++)
            Assert.Same(originalDescriptors[index], services[index]);
    }

    private static RepositoryConfiguration Settings(string keyword, string value) => new()
    {
        DatabaseProvider = "sqlite",
        ConnectionString = $"Data Source={DataSourceSentinel};Password={PasswordSentinel};{keyword}={value}"
    };

    private static void AssertSanitizedValidation(Error error, string invalidValue)
    {
        Assert.Equal(ErrorCategory.Validation, error.Category);
        foreach (var sensitiveValue in new[] { DataSourceSentinel, PasswordSentinel, invalidValue })
        {
            Assert.DoesNotContain(sensitiveValue, error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveValue, error.TechnicalMessage, StringComparison.Ordinal);
        }
    }
}
