using NyssaAI.Configuration.Client;
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
    public async Task ReadAsync_ExistingSecret_ResolvesUuidFromListAndReadsValue()
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
        Assert.Equal("resolved-db-secret", read.Value);
        Assert.Equal(2, calls.Count);
        Assert.Equal(["secret", "list", "proj-123", "--output", "json"], calls[0].Args);
        Assert.Equal(["secret", "get", "uuid-100", "--output", "json"], calls[1].Args);
        Assert.Equal("machine-token-abc", calls[0].Env[CloudKeyVaultBitwarden.AccessTokenEnvironmentVariable]);
        Assert.Equal("org-456", calls[0].Env[CloudKeyVaultBitwarden.OrganizationIdEnvironmentVariable]);
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
