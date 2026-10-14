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
    /// <returns>Validated settings. Configuration failures propagate unchanged.</returns>
    /// <exception cref="InvalidOperationException">The stored value deserializes to null instead of settings.</exception>
    /// <exception cref="ArgumentException">A Repository argument or settings value is invalid.</exception>
    /// <remarks>The store is borrowed and never disposed here. Configuration failures propagate unchanged, including recovery information.</remarks>
    /// <exception cref="OperationCanceledException">Reading is canceled; the exception carries the caller's token.</exception>
    public static async Task<RepositoryConfiguration> GetRepositoryConfigurationAsync(
        this ConfigurationStore configuration, string key = "default", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await configuration.GetAsAsync(ConfigurationNamespace, key,
            RepositoryConfigurationJsonContext.Default.RepositoryConfiguration, cancellationToken).ConfigureAwait(false);
        if (loaded is null) throw new InvalidOperationException("Repository settings are required.");
        loaded.Validate();
        return loaded;
    }

    /// <summary>Validates and saves settings using generated metadata and the store's protection rules.</summary>
    /// <param name="configuration">The caller's configuration store.</param>
    /// <param name="settings">The single repository settings record.</param>
    /// <param name="key">The repository instance's key within nyssa.repository.</param>
    /// <param name="storageProvider">Null preserves existing protection or uses default storage for a new entry. Changing destinations requires IConfiguration.MoveAsync.</param>
    /// <param name="cancellationToken">Cancels saving.</param>
    /// <returns>Completion after saving; failures throw and no database is opened.</returns>
    /// <exception cref="ArgumentException">A Repository argument or settings value is invalid.</exception>
    /// <remarks>The store is borrowed and never disposed here. Configuration failures propagate unchanged, including recovery information.</remarks>
    /// <exception cref="OperationCanceledException">Saving is canceled; the exception carries the caller's token.</exception>
    public static async Task SetRepositoryConfigurationAsync(this ConfigurationStore configuration,
        RepositoryConfiguration settings, string key = "default", string? storageProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        await configuration.SetAsAsync(ConfigurationNamespace, key, settings,
            RepositoryConfigurationJsonContext.Default.RepositoryConfiguration, storageProvider, cancellationToken).ConfigureAwait(false);
    }
}
