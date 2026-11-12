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

        ConfigurationServiceCollectionExtensions.SetManifestKeyVault(
            workstation.ManifestPath,
            new VaultOptions
            {
                Name = "bitwarden",
                Kind = "bitwarden-secrets",
                Endpoint = new Uri("https://api.bitwarden.com"),
                CacheTTLSeconds = 60,
                Settings =
                {
                    ["organizationId"] = "11111111-2222-3333-4444-555555555555",
                    ["projectId"] = "99999999-8888-7777-6666-555555555555",
                },
                Secrets =
                {
                    ["accessToken"] = "bws-machine-token-xyz",
                },
            },
            workstation.Keyring);

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

    [Fact]
    public async Task MultiVaultCoexistence_CorporateProdAndStagingBitwardenVaults_IsolatesProjectsAndMovesSecretsAsync()
    {
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        var tokenToProject = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["token-corp"] = "proj-corp",
            ["token-prod"] = "proj-prod",
            ["token-staging"] = "proj-staging",
        };
        var storesByProject = new Dictionary<string, Dictionary<string, (string Id, string Value)>>(StringComparer.Ordinal)
        {
            ["proj-corp"] = new(StringComparer.Ordinal),
            ["proj-prod"] = new(StringComparer.Ordinal),
            ["proj-staging"] = new(StringComparer.Ordinal),
        };
        var nextId = 0;

        ValueTask<Result<string>> MultiProjectRunner(
            string binary,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string> env,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!env.TryGetValue(CloudKeyVaultBitwarden.AccessTokenEnvironmentVariable, out var token)
                || !tokenToProject.TryGetValue(token, out var authorizedProject))
            {
                return ValueTask.FromResult(Result<string>.Failure(
                    CloudKeyVaultBitwarden.CommandFailed("bws", "unauthorized", ErrorCategory.Forbidden)));
            }

            var projectStore = storesByProject[authorizedProject];

            if (args is ["secret", "list", var listProject, "--output", "json"] && listProject == authorizedProject)
            {
                var items = projectStore.Select(kv => new { id = kv.Value.Id, key = kv.Key, value = kv.Value.Value }).ToList();
                return ValueTask.FromResult(Result<string>.Success(JsonSerializer.Serialize(items)));
            }

            if (args is ["secret", "create", var key, var val, var createProject, "--output", "json"] && createProject == authorizedProject)
            {
                var id = $"{authorizedProject}-uuid-{++nextId}";
                projectStore[key] = (id, val);
                return ValueTask.FromResult(Result<string>.Success(
                    JsonSerializer.Serialize(new { id, key, value = val })));
            }

            if (args is ["secret", "delete", var delId])
            {
                var match = projectStore.FirstOrDefault(kv => kv.Value.Id == delId);
                if (match.Key is not null)
                    projectStore.Remove(match.Key);
                return ValueTask.FromResult(Result<string>.Success(string.Empty));
            }

            return ValueTask.FromResult(Result<string>.Failure(
                CloudKeyVaultBitwarden.CommandFailed("bws", "unexpected command or project mismatch", ErrorCategory.Forbidden)));
        }

        foreach (var (vaultName, projectId, accessToken) in new[]
        {
            ("bws-corporate", "proj-corp", "token-corp"),
            ("bws-prod", "proj-prod", "token-prod"),
            ("bws-staging", "proj-staging", "token-staging"),
        })
        {
            ConfigurationServiceCollectionExtensions.SetManifestKeyVault(
                workstation.ManifestPath,
                new VaultOptions
                {
                    Name = vaultName,
                    Kind = "bitwarden-secrets",
                    Settings = { ["organizationId"] = "org-nyssa", ["projectId"] = projectId },
                    Secrets = { ["accessToken"] = accessToken },
                },
                workstation.Keyring);
        }

        var services = workstation.CreateServices();
        services.AddBitwardenSecretsVault(new CloudKeyVaultFactoryBitwarden(runner: MultiProjectRunner));
        services.AddNyssaConfiguration(workstation.Locate);
        workstation.StartApplication(services);
        var configuration = workstation.Host.GetRequiredService<IConfiguration>();
        var extensions = workstation.Host.GetRequiredService<IConfigurationExtensions>();

        var available = extensions.GetAvailableSecretProviders();
        Assert.Contains("bws-corporate", available);
        Assert.Contains("bws-prod", available);
        Assert.Contains("bws-staging", available);

        await configuration.SetAsync("billing", "stripeKey", "sk_corp_1", provider: "bws-corporate", cancellationToken: cancellationToken);
        await configuration.SetAsync("api", "signingKey", "sk_stage_2", provider: "bws-staging", cancellationToken: cancellationToken);

        Assert.Equal("sk_corp_1", await configuration.GetAsync("billing", "stripeKey", cancellationToken));
        Assert.Equal("sk_stage_2", await configuration.GetAsync("api", "signingKey", cancellationToken));
        Assert.True(storesByProject["proj-corp"].ContainsKey("billing/stripeKey"));
        Assert.True(storesByProject["proj-staging"].ContainsKey("api/signingKey"));
        Assert.Empty(storesByProject["proj-prod"]);

        var moved = await configuration.MoveAsync("api", "signingKey", "bws-prod", cancellationToken);
        Assert.Equal("bws-staging", moved.FromProvider);
        Assert.Equal("bws-prod", moved.ToProvider);
        Assert.False(moved.RemoteValueRemains);
        Assert.Empty(storesByProject["proj-staging"]);
        Assert.True(storesByProject["proj-prod"].ContainsKey("api/signingKey"));
        Assert.Equal("sk_stage_2", await configuration.GetAsync("api", "signingKey", cancellationToken));
    }
}
