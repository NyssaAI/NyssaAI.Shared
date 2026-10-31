namespace NyssaAI.DbGateway.Configuration;

/// <summary>DbGatewayConfiguration carries the selected database provider and its connection settings.</summary>
/// <remarks>Validation performs no I/O. Connection syntax and provider availability are checked during composition.</remarks>
public sealed record DbGatewayConfiguration
{
    private string _databaseProvider = string.Empty;

    /// <summary>Gets the provider key, trimmed and normalized using invariant lowercase rules (<c>"sqlite"</c> or <c>"postgresql"</c>).</summary>
    public required string DatabaseProvider
    {
        get => _databaseProvider;
        init => _databaseProvider = value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>Gets the connection string exactly as supplied, including whitespace and case.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Gets the default command timeout in seconds (0 for infinite; defaults to 30).</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Checks that the provider key, connection string, and timeout are valid without accessing external resources.</summary>
    /// <exception cref="ArgumentException">A required field is null, empty, whitespace, or negative.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabaseProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString);
        ArgumentOutOfRangeException.ThrowIfNegative(CommandTimeoutSeconds);
    }

    /// <summary>Returns the type name without exposing any configuration field values or credentials.</summary>
    public override string ToString() => nameof(DbGatewayConfiguration);
}
