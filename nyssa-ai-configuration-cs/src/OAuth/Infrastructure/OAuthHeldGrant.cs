using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>Holds a rotated OAuth grant in memory until it is persisted to the namespace document.</summary>
internal sealed record OAuthHeldGrant
{
    public required OAuthToken Token { get; init; }

    public required ConfigurationValue OriginalEntry { get; init; }
}
