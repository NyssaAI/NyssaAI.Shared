using NyssaAI.DbGateway.Configuration;

namespace NyssaAI.DbGateway.Providers;

/// <summary>DbProviderRegistration registers a compiled database provider factory for a named provider key.</summary>
public sealed record DbProviderRegistration
{
    private string _databaseProvider = string.Empty;

    /// <summary>Gets the provider identity, trimmed and normalized to lowercase.</summary>
    public required string DatabaseProvider
    {
        get => _databaseProvider;
        init => _databaseProvider = value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>Gets the preparation callback that validates settings and returns a deferred provider factory.</summary>
    public required Func<DbGatewayConfiguration, Func<IDbProvider>> Prepare { get; init; }

    /// <summary>Validates that the registration carries a non-empty provider name and preparation callback.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabaseProvider);
        ArgumentNullException.ThrowIfNull(Prepare);
    }
}
