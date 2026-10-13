using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Results;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>One DTO assembled from plaintext, encrypted, and vault-backed properties.</summary>
public sealed class NamespaceProvidersTutorials
{
    /// <summary>Writes preserve providers, JSON types, and serializer property names.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetAsAsync_MixedProviders_PreservesProvidersAndAssemblesDtoAsync(bool useSourceGeneration)
    {
        using var fixture = new Workstation();
        var vaults = new FakeVaultFactory();
        fixture.VaultPackage = vaults;
        fixture.AddNyssaConfiguration(configure: options => options.Bootstrap.KeyVault = new VaultOptions
        {
            Name = "mailVault", Kind = "fake",
        });
        var configuration = fixture.Host.GetRequiredService<IConfiguration>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new NamespaceMailSettings
        {
            Host = "smtp.example.test", Port = 587, UseTls = true,
            Password = "first-password", ApiKey = "first-api-key", RetryDelays = [1, 5],
        };
        // Choose secret storage before the first whole-object write.
        await configuration.SetAsync("mail", "Password", settings.Password, "aesLocal", cancellationToken);
        await configuration.SetAsync("mail", "api-key", settings.ApiKey, "mailVault", cancellationToken);
        await (useSourceGeneration
            ? configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken)
            : configuration.SetAsAsync("mail", settings, cancellationToken));

        var loaded = useSourceGeneration
            ? await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)
            : await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken);

        Assert.Equal(587, loaded.Port);
        Assert.True(loaded.UseTls);
        Assert.Null(loaded.Description);
        Assert.Equal([1, 5], loaded.RetryDelays);
        Assert.Equal(settings.Password, loaded.Password);
        Assert.Equal(settings.ApiKey, loaded.ApiKey);

        settings = settings with { Port = 465, Password = "next-password", ApiKey = "next-api-key", Host = "123" };
        await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken);
        var updated = await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken);
        Assert.Equal("123", updated.Host);
        Assert.Equal(465, updated.Port);
        Assert.Equal("next-password", updated.Password);
        Assert.Equal("next-api-key", updated.ApiKey);
        var text = fixture.ReadDocument("mail");
        Assert.DoesNotContain("next-password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("next-api-key", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        Assert.False(json.RootElement.TryGetProperty("namespaceSettings", out _));
        Assert.Equal("aesLocal", json.RootElement.GetProperty("Password").GetProperty("provider").GetString());
        Assert.Equal("mailVault", json.RootElement.GetProperty("api-key").GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("Port").GetProperty("value").ValueKind);

        // Keyed typed writes also remain usable in a whole-namespace read.
        await configuration.SetAsAsync("mail", "Port", 465, cancellationToken: cancellationToken);
        await configuration.SetAsAsync("mail", "Host", "123", AppJsonContext.Default.String, cancellationToken: cancellationToken);
        var keyed = await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken);
        Assert.Equal(465, keyed.Port);
        Assert.Equal("123", keyed.Host);
        Assert.Equal("123", await configuration.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken));

        // A moved number retains its JSON type through encryption, updates, a vault, and plaintext.
        await configuration.MoveAsync("mail", "Port", "aesLocal", cancellationToken);
        await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings with { Port = 2525 }, cancellationToken);
        await configuration.MoveAsync("mail", "Port", "mailVault", cancellationToken);
        Assert.Equal(2525, (await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)).Port);
        var vault = Assert.Single(vaults.Created);
        vault.Values["mail/Port"] = "invalid-json";
        await Assert.ThrowsAsync<ResultException>(() =>
            configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken).AsTask());
        vault.Values["mail/Port"] = "2525";
        await configuration.MoveAsync("mail", "Port", IConfiguration.Plain, cancellationToken);
        Assert.Equal(2525, (await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken)).Port);

        // A missing secret fails the whole read, rather than returning a partial DTO.
        Assert.Single(vaults.Created).Values.Remove("mail/api-key");
        await Assert.ThrowsAsync<ResultException>(async () =>
        {
            await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken);
        });
    }
}
