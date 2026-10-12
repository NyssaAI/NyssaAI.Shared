using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationPointerTests
{
    [Fact]
    public void Parse_WithExactlyTwoComponents_ReturnsPointer()
    {
        var result = ConfigurationPointer.Parse("vault", "secrets/apiKey");

        Assert.True(result.IsSuccess);
        Assert.Equal("vault", result.Value.Provider);
        Assert.Equal("secrets", result.Value.Namespace);
        Assert.Equal("apiKey", result.Value.Key);
        Assert.Equal("secrets/apiKey", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("namespace")]
    [InlineData("namespace/key/extra")]
    [InlineData("/key")]
    [InlineData("namespace/")]
    public void Parse_WithMalformedValue_ReturnsFailure(string value)
    {
        var result = ConfigurationPointer.Parse("vault", value);

        Assert.True(result.IsFailure);
        Assert.Equal(ConfigurationPointer.InvalidPointer, Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_WithBlankProvider_ReturnsFailure()
    {
        Assert.True(ConfigurationPointer.Parse("   ", "secrets/apiKey").IsFailure);
    }
}
