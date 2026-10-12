using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// What a vault package ships: a factory the developer registers, declaring its kind and — when the
/// vault signs its requests with OAuth2 — the authorization server it uses. The library does the
/// rest from the manifest.
/// </summary>
public sealed class FakeVaultFactory(OAuthOptions? authorization = null) : ICloudKeyVaultFactory
{
    public string Kind => "fake";

    /// <summary>Every vault this factory built, so a tutorial can look inside.</summary>
    public List<FakeVault> Created { get; } = [];

    public OAuthOptions? Authorization(VaultOptions settings) => authorization;

    public Result<ICloudKeyVault> Create(VaultOptions settings, IAccessTokenSource? tokens)
    {
        var vault = new FakeVault(settings, tokens);
        Created.Add(vault);
        return Result<ICloudKeyVault>.Success(vault);
    }
}

/// <summary>
/// An in-memory vault that behaves like a remote one: every operation first obtains a bearer token
/// from the source it was given, and it records which tokens it saw. It never holds a refresh
/// token, a cipher, or a key — that is the point of UC-015.
/// </summary>
public sealed class FakeVault(VaultOptions settings, IAccessTokenSource? tokens) : ICloudKeyVault
{
    public VaultOptions Settings => settings;

    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public List<string> BearerTokensSeen { get; } = [];

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public async ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        var signed = await SignAsync(cancellationToken);
        if (signed.IsFailure)
            return Result<string?>.Failure(signed.Errors);
        Reads++;
        return Result<string?>.Success(Values.GetValueOrDefault(pointer.Value));
    }

    public async ValueTask<Result<ConfigurationPointer>> WriteAsync(string @namespace, string key, string value, CancellationToken cancellationToken = default)
    {
        var signed = await SignAsync(cancellationToken);
        if (signed.IsFailure)
            return Result<ConfigurationPointer>.Failure(signed.Errors);
        Writes++;
        Values[$"{@namespace}/{key}"] = value;
        return ConfigurationPointer.Parse(settings.Name, $"{@namespace}/{key}");
    }

    public async ValueTask<Result> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        var signed = await SignAsync(cancellationToken);
        if (signed.IsFailure)
            return signed;
        Values.Remove(pointer.Value);
        return Result.Success();
    }

    // A real vault package puts this behind a DelegatingHandler that sets the Authorization header.
    private async ValueTask<Result> SignAsync(CancellationToken cancellationToken)
    {
        if (tokens is null)
            return Result.Success();
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        if (token.IsFailure)
            return Result.Failure(token.Errors);
        BearerTokensSeen.Add(token.Value);
        return Result.Success();
    }
}
