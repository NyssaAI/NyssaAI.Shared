using NyssaAI.Results;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.Configuration;


/// <summary>Persists the repository's single settings record through NyssaAI.Configuration.</summary>
public static class RepositoryConfigurationExtensions
{
    private const string ConfigurationNamespace = "nyssa.repository";


    /// <summary>Loads and validates settings without provisioning defaults or opening a database.</summary>
    /// <param name="configuration">The caller's configuration store.</param>
    /// <param name="key">The repository instance's key within nyssa.repository.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The settings or a sanitized failure retaining the configuration error category.</returns>
    public static async Task<Result<RepositoryConfiguration>> GetRepositoryConfigurationAsync(
        this ConfigurationStore configuration, string key = "default", CancellationToken cancellationToken = default)
    {
        var address = Guard.Against.False(configuration, store => store is not null && !string.IsNullOrWhiteSpace(key), "Configuration, settings, and a nonblank key are required.");
        if (address.IsFailure) return Result<RepositoryConfiguration>.Failure(address.Errors);
        configuration = address.Value;
        var active = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
        if (active.IsFailure) return Result<RepositoryConfiguration>.Failure(active.Errors);
        try
        {
            var loaded = await configuration.GetAsAsync(ConfigurationNamespace, key,
                RepositoryConfigurationJsonContext.Default.RepositoryConfiguration, cancellationToken).ConfigureAwait(false);
            var required = Guard.Against.False(loaded, settings => settings is not null, "Repository settings are required.");
            if (required.IsFailure) return Result<RepositoryConfiguration>.Failure(required.Errors);
            var validated = required.Value.Validate();
            return validated.IsFailure
                ? Result<RepositoryConfiguration>.Failure(validated.Errors)
                : Result<RepositoryConfiguration>.Success(required.Value);
        }
        catch (ResultException exception)
        {
            return Result<RepositoryConfiguration>.Failure(Sanitize(exception.Errors));
        }
        catch (OperationCanceledException)
        {
            return Result<RepositoryConfiguration>.Failure(Canceled());
        }
    }

    /// <summary>Validates and saves settings using generated metadata and the store's protection rules.</summary>
    /// <param name="configuration">The caller's configuration store.</param>
    /// <param name="settings">The single repository settings record.</param>
    /// <param name="key">The repository instance's key within nyssa.repository.</param>
    /// <param name="storageProvider">Null inherits existing protection; first save requires an explicit destination. Changing destinations requires IConfiguration.MoveAsync.</param>
    /// <param name="cancellationToken">Cancels saving.</param>
    /// <returns>A sanitized persistence or validation result; no database is opened.</returns>
    public static async Task<Result> SetRepositoryConfigurationAsync(this ConfigurationStore configuration,
        RepositoryConfiguration settings, string key = "default", string? storageProvider = null,
        CancellationToken cancellationToken = default)
    {
        var address = Guard.Against.False(configuration, store => store is not null && settings is not null && !string.IsNullOrWhiteSpace(key), "Configuration, settings, and a nonblank key are required.");
        if (address.IsFailure) return Result.Failure(address.Errors);
        configuration = address.Value;
        var active = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
        if (active.IsFailure) return active;
        var validated = settings.Validate();
        if (validated.IsFailure)
            return validated;
        try
        {
            await configuration.SetAsAsync(ConfigurationNamespace, key, settings,
                RepositoryConfigurationJsonContext.Default.RepositoryConfiguration, storageProvider, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (ResultException exception)
        {
            return Result.Failure(Sanitize(exception.Errors));
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(Canceled());
        }
    }

    // The underlying error's technical message may quote malformed JSON or secret-provider input.
    private static IEnumerable<Error> Sanitize(IReadOnlyList<Error> errors) => errors.Select(error =>
        Error.FromMessage("Repository configuration could not be accessed.", error.Category));

    private static Error Canceled() => Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled);
}
