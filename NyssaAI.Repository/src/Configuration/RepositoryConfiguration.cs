using NyssaAI.Results;

namespace NyssaAI.Repository.Configuration;

/// <summary>RepositoryConfiguration carries the selected provider and its connection settings.</summary>
/// <remarks>Validation performs no I/O. Connection syntax and provider availability are checked during composition.</remarks>
public sealed record RepositoryConfiguration
{
    private string _databaseProvider = string.Empty;

    /// <summary>Gets the provider key, trimmed and normalized using invariant lowercase rules.</summary>
    public required string DatabaseProvider
    {
        get => _databaseProvider;
        init => _databaseProvider = value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>Gets the connection string exactly as supplied, including whitespace and case.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Checks that the provider key and connection string are present without accessing external resources.</summary>
    /// <returns>A validation failure for a null, empty, or whitespace field; otherwise success.</returns>
    public Result Validate()
    {
        var provider = Guard.Against.NullOrWhiteSpace(DatabaseProvider, nameof(DatabaseProvider));
        if (provider.IsFailure) return Result.Failure(provider.Errors);
        var connection = Guard.Against.NullOrWhiteSpace(ConnectionString, nameof(ConnectionString));
        return connection.IsFailure ? Result.Failure(connection.Errors) : Result.Success();
    }

    /// <summary>Returns the type name without exposing any configuration field values.</summary>
    public override string ToString() => nameof(RepositoryConfiguration);
}
