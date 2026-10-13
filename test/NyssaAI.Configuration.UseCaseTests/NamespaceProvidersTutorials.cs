using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
        (await configuration.SetAsync("mail", "Password", settings.Password, "aesLocal", cancellationToken)).ThrowIfError();
        (await configuration.SetAsync("mail", "api-key", settings.ApiKey, "mailVault", cancellationToken)).ThrowIfError();
        var saved = useSourceGeneration
            ? await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken)
            : await configuration.SetAsAsync("mail", settings, cancellationToken);
        saved.ThrowIfError();
        var loaded = useSourceGeneration
            ? await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)
            : await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken);
        loaded.ThrowIfError();
        Assert.Equal(587, loaded.Value.Port);
        Assert.True(loaded.Value.UseTls);
        Assert.Null(loaded.Value.Description);
        Assert.Equal([1, 5], loaded.Value.RetryDelays);
        Assert.Equal(settings.Password, loaded.Value.Password);
        Assert.Equal(settings.ApiKey, loaded.Value.ApiKey);

        settings = settings with { Port = 465, Password = "next-password", ApiKey = "next-api-key", Host = "123" };
        (await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken)).ThrowIfError();
        var updated = (await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)).ThrowIfError();
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
        (await configuration.SetAsAsync("mail", "Port", 465, cancellationToken: cancellationToken)).ThrowIfError();
        (await configuration.SetAsAsync("mail", "Host", "123", AppJsonContext.Default.String, cancellationToken: cancellationToken)).ThrowIfError();
        var keyed = (await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)).ThrowIfError();
        Assert.Equal(465, keyed.Port);
        Assert.Equal("123", keyed.Host);
        Assert.Equal("123", (await configuration.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken)).ThrowIfError());

        // A moved number retains its JSON type through encryption, updates, a vault, and plaintext.
        (await configuration.MoveAsync("mail", "Port", "aesLocal", cancellationToken)).ThrowIfError();
        (await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings with { Port = 2525 }, cancellationToken)).ThrowIfError();
        (await configuration.MoveAsync("mail", "Port", "mailVault", cancellationToken)).ThrowIfError();
        Assert.Equal(2525, (await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)).ThrowIfError().Port);
        var vault = Assert.Single(vaults.Created);
        vault.Values["mail/Port"] = "invalid-json";
        Assert.True((await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)).IsFailure);
        vault.Values["mail/Port"] = "2525";
        (await configuration.MoveAsync("mail", "Port", IConfiguration.Plain, cancellationToken)).ThrowIfError();
        Assert.Equal(2525, (await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken)).ThrowIfError().Port);

        // A missing secret fails the whole read, rather than returning a partial DTO.
        Assert.Single(vaults.Created).Values.Remove("mail/api-key");
        var missing = await configuration.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken);
        Assert.True(missing.IsFailure);
    }
}
