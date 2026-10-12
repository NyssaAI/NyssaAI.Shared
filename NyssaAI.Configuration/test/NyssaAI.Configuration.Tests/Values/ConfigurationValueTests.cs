using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationValueTests
{
    [Fact]
    public void Validate_WithEncryptedLiteral_ReturnsFailure()
    {
        var entry = new ConfigurationValue { Value = "ciphertext", Encrypted = true };

        var result = entry.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal(ConfigurationValue.EncryptedEntryNeedsProvider, Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_WithEncryptedProviderEntry_ReturnsSuccess()
    {
        var entry = new ConfigurationValue { Value = "ciphertext", Encrypted = true, Provider = "aesLocal" };

        Assert.True(entry.Validate().IsSuccess);
    }

    [Fact]
    public void LiteralEntry_IsLiteralAndValid()
    {
        var entry = new ConfigurationValue { Value = "value" };

        Assert.True(entry.IsLiteral);
        Assert.True(entry.Validate().IsSuccess);
    }
}
