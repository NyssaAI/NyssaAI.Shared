using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ProviderCredential records which entry a provider authenticates with, as the manifest declared
/// it, and the server it signs in to. The store uses it for two things: refusing a write that would
/// put the credential inside the provider it unlocks, and answering the provider's token source.
/// </summary>
internal sealed record ProviderCredential
{
    /// <summary>Gets the provider that authenticates with this credential.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the namespace half of the credential's address.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the key half of the credential's address.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the server the provider signs in to, when it authenticates with OAuth2.</summary>
    public OAuthOptions? Authorization { get; init; }

    public string Address => $"{Namespace}/{Key}";
}
