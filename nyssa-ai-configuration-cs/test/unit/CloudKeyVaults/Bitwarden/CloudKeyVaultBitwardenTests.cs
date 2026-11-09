using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.CloudKeyVaults.Bitwarden;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.CloudKeyVaults.Bitwarden;

public sealed class CloudKeyVaultBitwardenTests
{
    private const string ListResponseJson = """
        [
          {"id":"uuid-100","key":"db/password","value":"old-secret"},
          {"id":"uuid-200","slug":"api.token"}
        ]
        """;

    private const string GetResponseJson = """
        {"id":"uuid-100","key":"db/password","value":"resolved-db-secret"}
        """;

    [Fact]
    public async Task ReadAsync_ExistingSecretWithInlineValue_ReturnsValueFromListWithoutCallingSecretGet()
    {
        var calls = new List<(string Binary, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env)>();
        var settings = CreateOptions();
        var vault = new CloudKeyVaultBitwarden(
            settings,
            runner: (binary, args, env, _) =>
            {
                calls.Add((binary, args, env));
                var output = args[1] == "list" ? ListResponseJson : GetResponseJson;
                return ValueTask.FromResult(Result<string>.Success(output));
            });
        var pointer = ConfigurationPointer.Parse("bwsVault", "db/password").Value;

        var read = await vault.ReadAsync(pointer, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccess);
        Assert.Equal("old-secret", read.Value);
        var call = Assert.Single(calls);
        Assert.Equal(["secret", "list", "proj-123", "--output", "json"], call.Args);
        Assert.Equal("machine-token-abc", call.Env[CloudKeyVaultBitwarden.AccessTokenEnvironmentVariable]);
        Assert.Equal("org-456", call.Env[CloudKeyVaultBitwarden.OrganizationIdEnvironmentVariable]);
    }

    [Fact]
    public async Task ReadAsync_ExistingSecretWithoutInlineValue_FallsBackToSecretGetByUuid()
    {
        var calls = new List<(string Binary, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env)>();
        var settings = CreateOptions();
        var vault = new CloudKeyVaultBitwarden(
            settings,
            runner: (binary, args, env, _) =>
            {
                calls.Add((binary, args, env));
                var output = args[1] == "list" ? ListResponseJson : """{"id":"uuid-200","slug":"api.token","value":"resolved-api-token"}""";
                return ValueTask.FromResult(Result<string>.Success(output));
            });
        var pointer = ConfigurationPointer.Parse("bwsVault", "api/token").Value;

        var read = await vault.ReadAsync(pointer, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccess);
        Assert.Equal("resolved-api-token", read.Value);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["secret", "list", "proj-123", "--output", "json"], calls[0].Args);
        Assert.Equal(["secret", "get", "uuid-200", "--output", "json"], calls[1].Args);
    }

    [Fact]
    public async Task CachingDecorator_PreWarmsSiblingSecretsAndEvictsOnWriteAndRemove()
    {
        var calls = 0;
        var currentSecret = "initial-db-secret";
        var settings = CreateOptions();
        var inner = new CloudKeyVaultBitwarden(
            settings,
            runner: (_, args, _, _) =>
            {
                calls++;
                if (args[1] == "list")
                {
                    return ValueTask.FromResult(Result<string>.Success(
                        $$"""[{"id":"uuid-100","key":"db/password","value":"{{currentSecret}}"},{"id":"uuid-101","key":"db/user","value":"postgres"}]"""));
                }
                return ValueTask.FromResult(Result<string>.Success(
                    $$"""{"id":"uuid-100","key":"db/password","value":"{{currentSecret}}"}"""));
            });
        var cached = new CloudKeyVaultCaching(inner, cacheTTLSeconds: 60, cache: new SecretCacheInMemory(), cacheScope: "bwsVault:bitwarden-secrets:proj-123");
        var pwPointer = ConfigurationPointer.Parse("bwsVault", "db/password").Value;
        var userPointer = ConfigurationPointer.Parse("bwsVault", "db/user").Value;

        var firstRead = await cached.ReadAsync(pwPointer, TestContext.Current.CancellationToken);
        var secondRead = await cached.ReadAsync(pwPointer, TestContext.Current.CancellationToken);
        var siblingRead = await cached.ReadAsync(userPointer, TestContext.Current.CancellationToken);

        Assert.Equal("initial-db-secret", firstRead.Value);
        Assert.Equal("initial-db-secret", secondRead.Value);
        Assert.Equal("postgres", siblingRead.Value);
        Assert.Equal(1, calls);

        currentSecret = "rotated-db-secret";
        var written = await cached.WriteAsync("db", "password", currentSecret, TestContext.Current.CancellationToken);
        Assert.True(written.IsSuccess);

        var callsBeforeAfterWriteRead = calls;
        var afterWriteRead = await cached.ReadAsync(pwPointer, TestContext.Current.CancellationToken);
        Assert.Equal("rotated-db-secret", afterWriteRead.Value);
        Assert.Equal(callsBeforeAfterWriteRead + 1, calls);
    }

    [Fact]
    public async Task ReadAsync_AbsentSecret_ReturnsSuccessWithNull()
    {
        var vault = new CloudKeyVaultBitwarden(
            CreateOptions(),
            runner: (_, _, _, _) => ValueTask.FromResult(Result<string>.Success(ListResponseJson)));
        var pointer = ConfigurationPointer.Parse("bwsVault", "missing/key").Value;

        var read = await vault.ReadAsync(pointer, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccess);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task ReadAsync_EmptyProject404_TreatsProjectAsEmptyAndReturnsNull()
    {
        var vault = new CloudKeyVaultBitwarden(
            CreateOptions(),
            runner: (_, _, _, _) => ValueTask.FromResult(Result<string>.Failure(
                CloudKeyVaultBitwarden.CommandFailed("bwsVault", "[404 Not Found] Resource not found."))));
        var pointer = ConfigurationPointer.Parse("bwsVault", "db/password").Value;

        var read = await vault.ReadAsync(pointer, TestContext.Current.CancellationToken);

        Assert.True(read.IsSuccess);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task WriteAsync_NewSecret_CreatesSecretScopedToProjectAndReturnsPointer()
    {
        var calls = new List<IReadOnlyList<string>>();
        var vault = new CloudKeyVaultBitwarden(
            CreateOptions(),
            runner: (_, args, _, _) =>
            {
                calls.Add(args);
                var json = args[1] == "list" ? "[]" : """{"id":"uuid-new","key":"billing/stripeKey","value":"sk_live"}""";
                return ValueTask.FromResult(Result<string>.Success(json));
            });

        var written = await vault.WriteAsync("billing", "stripeKey", "sk_live", TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccess);
        Assert.Equal("billing/stripeKey", written.Value.Value);
        Assert.Equal("bwsVault", written.Value.Provider);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["secret", "create", "billing/stripeKey", "sk_live", "proj-123", "--output", "json"], calls[1]);
    }

    [Fact]
    public async Task WriteAsync_ExistingSecret_EditsExistingSecretByUuid()
    {
        var calls = new List<IReadOnlyList<string>>();
        var vault = new CloudKeyVaultBitwarden(
            CreateOptions(),
            runner: (_, args, _, _) =>
            {
                calls.Add(args);
                return ValueTask.FromResult(Result<string>.Success(ListResponseJson));
            });

        var written = await vault.WriteAsync("db", "password", "rotated-secret", TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccess);
        Assert.Equal("db/password", written.Value.Value);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["secret", "edit", "uuid-100", "--value", "rotated-secret", "--output", "json"], calls[1]);
    }

    [Fact]
    public async Task WriteAsync_MissingProjectIdOnCreate_ReturnsValidationFailure()
    {
        var options = new VaultOptions { Name = "bwsVault", Kind = CloudKeyVaultFactoryBitwarden.DefaultKind };
        options.Secrets["accessToken"] = "machine-token";
        var vault = new CloudKeyVaultBitwarden(
            options,
            runner: (_, _, _, _) => ValueTask.FromResult(Result<string>.Success("[]")));

        var written = await vault.WriteAsync("db", "newKey", "val", TestContext.Current.CancellationToken);

        Assert.True(written.IsFailure);
        Assert.Equal(ErrorCategory.Validation, written.Errors[0].Category);
    }

    [Fact]
    public async Task RemoveAsync_ExistingSecret_DeletesByUuidAndReportsNoRetention()
    {
        var calls = new List<IReadOnlyList<string>>();
        var vault = new CloudKeyVaultBitwarden(
            CreateOptions(),
            runner: (_, args, _, _) =>
            {
                calls.Add(args);
                return ValueTask.FromResult(Result<string>.Success(args[1] == "list" ? ListResponseJson : ""));
            });
        var pointer = ConfigurationPointer.Parse("bwsVault", "db/password").Value;

        var removed = await vault.RemoveAsync(pointer, TestContext.Current.CancellationToken);

        Assert.True(removed.IsSuccess);
        Assert.False(removed.Value.RemoteValueRemains);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["secret", "delete", "uuid-100"], calls[1]);
    }

    [Fact]
    public void FactoryCreate_WithoutTokenOrCredential_ReturnsValidationFailure()
    {
        var factory = new CloudKeyVaultFactoryBitwarden();
        var options = new VaultOptions { Name = "bwsVault", Kind = CloudKeyVaultFactoryBitwarden.DefaultKind };

        var created = factory.Create(options, tokens: null);

        Assert.True(created.IsFailure);
        Assert.Equal(ErrorCategory.Validation, created.Errors[0].Category);
    }

    private static VaultOptions CreateOptions()
    {
        var options = new VaultOptions
        {
            Name = "bwsVault",
            Kind = CloudKeyVaultFactoryBitwarden.DefaultKind,
            Endpoint = new Uri("https://api.bitwarden.com"),
        };
        options.Settings["projectId"] = "proj-123";
        options.Settings["organizationId"] = "org-456";
        options.Secrets["accessToken"] = "machine-token-abc";
        return options;
    }
}
