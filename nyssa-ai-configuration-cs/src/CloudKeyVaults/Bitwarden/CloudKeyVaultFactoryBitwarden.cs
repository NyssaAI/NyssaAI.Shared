using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.CloudKeyVaults.Bitwarden;

/// <summary>
/// <see cref="ICloudKeyVaultFactory"/> that builds <see cref="CloudKeyVaultBitwarden"/> instances for Bitwarden Secrets Manager.
/// </summary>
public sealed class CloudKeyVaultFactoryBitwarden : ICloudKeyVaultFactory
{
    /// <summary>The canonical manifest kind for Bitwarden Secrets Manager.</summary>
    public const string DefaultKind = "bitwarden-secrets";

    /// <summary>The short manifest kind alias for Bitwarden Secrets Manager CLI.</summary>
    public const string ShortKind = "bws";

    /// <summary>The legacy manifest kind alias for Bitwarden.</summary>
    public const string LegacyKind = "bitwarden";

    private static readonly Uri DefaultAuthorizeEndpoint = new("https://identity.bitwarden.com/connect/authorize");
    private static readonly Uri DefaultTokenEndpoint = new("https://identity.bitwarden.com/connect/token");

    private readonly Func<string, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Result<string>>>? _runner;
    private readonly OAuthOptions? _authorization;

    /// <summary>
    /// Creates a factory for Bitwarden Secrets Manager vaults.
    /// </summary>
    /// <param name="kind">The vault kind string matched against <see cref="VaultOptions.Kind"/>.</param>
    /// <param name="authorization">Optional explicit OAuth 2.0 options for interactive sign-in.</param>
    /// <param name="runner">Optional CLI process runner delegate for deterministic testing.</param>
    public CloudKeyVaultFactoryBitwarden(
        string kind = DefaultKind,
        OAuthOptions? authorization = null,
        Func<string, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<Result<string>>>? runner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        _authorization = authorization;
        _runner = runner;
    }

    /// <inheritdoc/>
    public string Kind { get; }

    /// <inheritdoc/>
    public OAuthOptions? Authorization(VaultOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_authorization is not null)
            return _authorization;

        var authorizeUri = TryParseHttpsUri(settings.Settings, "authorizationEndpoint") ?? DefaultAuthorizeEndpoint;
        var tokenUri = TryParseHttpsUri(settings.Settings, "tokenEndpoint") ?? DefaultTokenEndpoint;
        var clientId = settings.Settings.TryGetValue("clientId", out var id) && !string.IsNullOrWhiteSpace(id)
            ? id.Trim()
            : "bitwarden-secrets-cli";

        return new OAuthOptions
        {
            AuthorizationEndpoint = authorizeUri,
            TokenEndpoint = tokenUri,
            ClientId = clientId,
        };
    }

    /// <inheritdoc/>
    public Result<ICloudKeyVault> Create(VaultOptions settings, IAccessTokenSource? tokens)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return Result<ICloudKeyVault>.Failure(VaultNameRequired);

        var hasStaticToken = (settings.Secrets.TryGetValue("accessToken", out var accessToken) && !string.IsNullOrWhiteSpace(accessToken))
            || (settings.Secrets.TryGetValue("machineAccessToken", out var machineToken) && !string.IsNullOrWhiteSpace(machineToken));

        if (tokens is null && !hasStaticToken)
            return Result<ICloudKeyVault>.Failure(CloudKeyVaultBitwarden.AccessTokenRequired(settings.Name));

        return Result<ICloudKeyVault>.Success(new CloudKeyVaultBitwarden(settings, tokens, _runner));
    }

    /// <summary>The failure returned when the vault options have a blank provider name.</summary>
    public static Error VaultNameRequired { get; } = Error.FromMessages(
        "The configured vault name is not valid.",
        "vault: VaultOptions.Name must not be blank",
        ErrorCategory.Validation);

    private static Uri? TryParseHttpsUri(IDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var raw)
        && Uri.TryCreate(raw, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
}
