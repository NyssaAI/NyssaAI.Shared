
using NyssaAI.Configuration.Encryption;

namespace NyssaAI.Configuration.Tests.Encryption;
public sealed class ConfigurationCipherAesGcmTests
{
    [Fact]
    public void SealAndOpen_WithSameAddress_RoundTrips()
    {
        var cipher = CreateCipher();

        var sealedValue = cipher.Seal("secret", "service", "password");

        Assert.True(sealedValue.IsSuccess);
        var opened = cipher.Open(sealedValue.Value, "service", "password");
        Assert.True(opened.IsSuccess);
        Assert.Equal("secret", opened.Value);
    }

    [Fact]
    public void Open_WithDifferentAddress_ReturnsFailure()
    {
        var cipher = CreateCipher();
        var sealedValue = cipher.Seal("secret", "service", "password");

        var opened = cipher.Open(sealedValue.Value, "other", "password");

        Assert.True(opened.IsFailure);
        Assert.Equal(ConfigurationCipherAesGcm.SealedValueNotAuthentic, Assert.Single(opened.Errors));
    }

    [Fact]
    public void Open_WithTamperedValue_ReturnsFailure()
    {
        var cipher = CreateCipher();
        var sealedValue = cipher.Seal("secret", "service", "password").Value;

        // Tamper a character in the middle, not the last one. The sealed form is base64url with
        // padding stripped, and the payload's length leaves the final character carrying only a
        // few significant bits -- its low bits are padding. Flipping that character between 'A'
        // and 'B' changes only padding bits about one time in sixteen, decoding to the identical
        // payload, and Open then succeeds exactly as it should. Every earlier character carries
        // six significant bits, so changing one always alters the nonce, tag or ciphertext.
        var index = sealedValue.Length / 2;
        var tampered = sealedValue[..index]
            + (sealedValue[index] == 'A' ? 'B' : 'A')
            + sealedValue[(index + 1)..];

        Assert.True(cipher.Open(tampered, "service", "password").IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Create_WithWrongKeyLength_ReturnsFailure(int keyLength)
    {
        var result = ConfigurationCipherAesGcm.Create(new byte[keyLength]);

        Assert.True(result.IsFailure);
        Assert.Equal(ConfigurationCipherAesGcm.KeyLengthInvalid(keyLength), Assert.Single(result.Errors));
    }

    private static ConfigurationCipherAesGcm CreateCipher()
    {
        var created = ConfigurationCipherAesGcm.Create(new byte[32]);
        Assert.True(created.IsSuccess);
        return created.Value;
    }
}
