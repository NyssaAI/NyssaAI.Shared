using System.Text.Json;
using NyssaAI.Configuration.Values;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationValueJsonConverterTests
{
    [Theory]
    [InlineData("""{"value":"text","encrypted":false,"provider":"","reauth_required":false}""", """{"value":"text"}""")]
    [InlineData("""{"value":"null"}""", """{"value":"null"}""")]
    [InlineData("""{"value":"text","json":true,"encrypted":false,"provider":""}""", """{"value":"text","json":true}""")]
    [InlineData("""{"value":587,"encrypted":false,"provider":""}""", """{"value":587}""")]
    [InlineData("""{"value":true,"reauth_required":false}""", """{"value":true}""")]
    [InlineData("""{"value":null}""", """{"value":null}""")]
    [InlineData("""{"value":{"Host":"smtp.example.test","Retry":{"Attempts":3}},"encrypted":false,"provider":""}""", """{"value":{"Host":"smtp.example.test","Retry":{"Attempts":3}}}""")]
    [InlineData("""{"value":[1,5],"json":true}""", """{"value":[1,5]}""")]
    [InlineData("""{"value":"ciphertext","encrypted":true,"provider":"aesLocal","json":true,"reauth_required":false}""", """{"value":"ciphertext","encrypted":true,"provider":"aesLocal","json":true}""")]
    [InlineData("""{"value":"mail/server","provider":"vault","encrypted":false,"json":true}""", """{"value":"mail/server","provider":"vault","json":true}""")]
    [InlineData("""{"value":"credential","provider":"oauth","encrypted":true,"reauth_required":true}""", """{"value":"credential","provider":"oauth","encrypted":true,"reauth_required":true}""")]
    public void Write_ExistingEntry_OmitsDefaultMetadataAndPreservesMeaning(string input, string expected)
    {
        var typeInfo = ConfigurationJsonContext.Default.DictionaryStringConfigurationValue;
        var entries = JsonSerializer.Deserialize("{\"entry\":" + input + "}", typeInfo);
        Assert.NotNull(entries);

        var serialized = JsonSerializer.Serialize(entries, typeInfo);

        using var actualJson = JsonDocument.Parse(serialized);
        using var expectedJson = JsonDocument.Parse(expected);
        Assert.True(JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement.GetProperty("entry")));
        var reloaded = JsonSerializer.Deserialize(serialized, typeInfo);
        Assert.NotNull(reloaded);
        var original = entries["entry"];
        var restored = reloaded["entry"];
        Assert.Equal(original.Provider, restored.Provider);
        Assert.Equal(original.Encrypted, restored.Encrypted);
        Assert.Equal(original.ReauthRequired, restored.ReauthRequired);
        Assert.Equal(original.IsJsonValue, restored.IsJsonValue);
        if (original.IsJsonValue && original.IsLiteral)
        {
            using var originalValue = JsonDocument.Parse(original.Value);
            using var restoredValue = JsonDocument.Parse(restored.Value);
            Assert.True(JsonElement.DeepEquals(originalValue.RootElement, restoredValue.RootElement));
        }
        else
            Assert.Equal(original.Value, restored.Value);
    }
}
