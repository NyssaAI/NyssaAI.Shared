using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.CloudKeyVaults.Bitwarden;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// End-to-end use-case tutorials exercising the built-in Bitwarden Secrets Manager (<c>bws</c>) cloud key vault provider.
/// </summary>
public sealed class BitwardenVaultTutorials
{
    [Fact]
    public async Task SetAndGetAsync_BitwardenSecretsVault_StoresPointerLocallyAndResolvesRemoteSecretAsync()
    {
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;
        var remoteStore = new Dictionary<string, (string Id, string Value)>(StringComparer.Ordinal);
        var nextId = 0;
        var listCalls = 0;

        ValueTask<Result<string>> Runner(
            string binary,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string> env,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!env.TryGetValue(CloudKeyVaultBitwarden.AccessTokenEnvironmentVariable, out var token)
                || token != "bws-machine-token-xyz")
            {
                return ValueTask.FromResult(Result<string>.Failure(
                    CloudKeyVaultBitwarden.CommandFailed("bitwarden", "unauthorized", ErrorCategory.Forbidden)));
            }

            if (args is ["secret", "list", ..])
            {
                listCalls++;
                var items = remoteStore.Select(kv => new { id = kv.Value.Id, key = kv.Key, value = kv.Value.Value }).ToList();
                return ValueTask.FromResult(Result<string>.Success(JsonSerializer.Serialize(items)));
            }

            if (args is ["secret", "create", var key, var val, _, "--output", "json"])
            {
                var id = $"bws-uuid-{++nextId}";
                remoteStore[key] = (id, val);
                return ValueTask.FromResult(Result<string>.Success(
                    JsonSerializer.Serialize(new { id, key, value = val })));
            }

            if (args is ["secret", "edit", var editId, "--value", var newVal, "--output", "json"])
            {
                var entry = remoteStore.First(kv => kv.Value.Id == editId);
                remoteStore[entry.Key] = (editId, newVal);
                return ValueTask.FromResult(Result<string>.Success(
                    JsonSerializer.Serialize(new { id = editId, key = entry.Key, value = newVal })));
            }

            if (args is ["secret", "get", var getId, "--output", "json"])
            {
                var match = remoteStore.FirstOrDefault(kv => kv.Value.Id == getId);
                return match.Key is null
                    ? ValueTask.FromResult(Result<string>.Failure(CloudKeyVaultBitwarden.CommandFailed("bitwarden", "404 Not Found")))
                    : ValueTask.FromResult(Result<string>.Success(
                        JsonSerializer.Serialize(new { id = getId, key = match.Key, value = match.Value.Value })));
            }

            if (args is ["secret", "delete", var delId])
            {
                var match = remoteStore.FirstOrDefault(kv => kv.Value.Id == delId);
                if (match.Key is not null)
                    remoteStore.Remove(match.Key);
                return ValueTask.FromResult(Result<string>.Success(string.Empty));
            }

            return ValueTask.FromResult(Result<string>.Failure(
                CloudKeyVaultBitwarden.CommandFailed("bitwarden", "unexpected command")));
        }

        workstation.WriteManifest(
            """
            {
              "version": 1,
              "applications": {
                "SomeApp-cli": {
                  "configurationDirectory": "SomeApp-cli",
                  "keyVault": {
                    "name": "bitwarden",
                    "kind": "bitwarden-secrets",
                    "endpoint": "https://api.bitwarden.com",
                    "cacheTTLSeconds": 60,
                    "settings": {
                      "organizationId": "11111111-2222-3333-4444-555555555555",
                      "projectId": "99999999-8888-7777-6666-555555555555"
                    },
                    "secrets": {
                      "accessToken": "bws-machine-token-xyz"
                    }
                  }
                }
              }
            }
            """);

        ISecretCache secretCache = new SecretCacheInMemory(workstation.Clock);
        var services = workstation.CreateServices();
        services.AddSingleton(secretCache);
        services.AddBitwardenSecretsVault(new CloudKeyVaultFactoryBitwarden(runner: Runner));
        services.AddNyssaConfiguration(workstation.Locate);
        workstation.StartApplication(services);
        var configuration = workstation.Host.GetRequiredService<IConfiguration>();

        await configuration.SetAsync(
            "payments",
            "webhookSecret",
            "whsec_live_bws_987",
            provider: "bitwarden",
            cancellationToken: cancellationToken);
        var listCallsAfterWrite = listCalls;
        var resolved = await configuration.GetAsync("payments", "webhookSecret", cancellationToken);
        var resolvedCached = await configuration.GetAsync("payments", "webhookSecret", cancellationToken);
        Assert.Equal(listCallsAfterWrite + 1, listCalls);

        workstation.Clock.Advance(TimeSpan.FromSeconds(CloudKeyVaultCaching.DefaultCacheTTLSeconds + 1));
        var resolvedAfterExpiry = await configuration.GetAsync("payments", "webhookSecret", cancellationToken);
        Assert.Equal(listCallsAfterWrite + 2, listCalls);

        var removal = await configuration.RemoveAsync("payments", "webhookSecret", cancellationToken);

        Assert.Equal("whsec_live_bws_987", resolved);
        Assert.Equal("whsec_live_bws_987", resolvedCached);
        Assert.Equal("whsec_live_bws_987", resolvedAfterExpiry);
        Assert.False(removal.RemoteValueRemains);
        Assert.Empty(remoteStore);
    }
}
