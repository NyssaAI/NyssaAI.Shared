using System.Diagnostics.CodeAnalysis;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// Sanitized metadata receipt returned after a secret has been captured and stored.
/// Plaintext secrets and masked previews are strictly excluded from this payload.
/// </summary>
public sealed record SecretReceipt
{
    /// <summary>Namespace of the configuration entry.</summary>
    public required string Namespace { get; init; }

    /// <summary>Key name of the configuration entry.</summary>
    public required string Key { get; init; }

    /// <summary>Storage provider selected and used (e.g. "aesLocal" or a loaded vault name).</summary>
    public required string Provider { get; init; }

    /// <summary>Timestamp when the secret was securely persisted.</summary>
    public required DateTimeOffset StoredAt { get; init; }

    /// <summary>Initializes a new instance of <see cref="SecretReceipt"/>.</summary>
    [SetsRequiredMembers]
    public SecretReceipt(string @namespace, string key, string provider, DateTimeOffset storedAt)
    {
        Namespace = @namespace;
        Key = key;
        Provider = provider;
        StoredAt = storedAt;
    }

    /// <summary>Initializes a new instance of <see cref="SecretReceipt"/>.</summary>
    public SecretReceipt()
    {
    }
}
