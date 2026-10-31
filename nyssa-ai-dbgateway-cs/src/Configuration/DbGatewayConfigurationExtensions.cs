using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.DbGateway.Configuration;

/// <summary>Persists and loads <see cref="DbGatewayConfiguration"/> through <see cref="ConfigurationStore"/>.</summary>
public static class DbGatewayConfigurationExtensions
{
    private const string ConfigurationNamespace = "nyssa.dbgateway";

    /// <summary>Loads and validates database gateway settings without provisioning defaults or opening a database.</summary>
    /// <param name="configuration">The caller's configuration store.</param>
    /// <param name="key">The gateway instance's key within <c>nyssa.dbgateway</c>.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>Validated settings.</returns>
    /// <exception cref="InvalidOperationException">The stored value deserializes to null instead of settings.</exception>
    /// <exception cref="ArgumentException">An argument or settings value is invalid.</exception>
    /// <exception cref="OperationCanceledException">Reading is canceled.</exception>
    public static async Task<DbGatewayConfiguration> GetDbGatewayConfigurationAsync(
        this ConfigurationStore configuration,
        string key = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await configuration.GetAsAsync(
            ConfigurationNamespace,
            key,
            DbGatewayConfigurationJsonContext.Default.DbGatewayConfiguration,
            cancellationToken).ConfigureAwait(false);
        if (loaded is null)
            throw new InvalidOperationException("Database gateway settings are required.");
        loaded.Validate();
        return loaded;
    }

    /// <summary>Validates and saves database gateway settings using generated metadata and the store's protection rules.</summary>
    /// <param name="configuration">The caller's configuration store.</param>
    /// <param name="settings">The database gateway settings record.</param>
    /// <param name="key">The gateway instance's key within <c>nyssa.dbgateway</c>.</param>
    /// <param name="storageProvider">Null preserves existing protection or uses default storage for a new entry.</param>
    /// <param name="cancellationToken">Cancels saving.</param>
    /// <returns>Completion after saving.</returns>
    /// <exception cref="ArgumentException">An argument or settings value is invalid.</exception>
    /// <exception cref="OperationCanceledException">Saving is canceled.</exception>
    public static async Task SetDbGatewayConfigurationAsync(
        this ConfigurationStore configuration,
        DbGatewayConfiguration settings,
        string key = "default",
        string? storageProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        await configuration.SetAsAsync(
            ConfigurationNamespace,
            key,
            settings,
            DbGatewayConfigurationJsonContext.Default.DbGatewayConfiguration,
            storageProvider,
            cancellationToken).ConfigureAwait(false);
    }
}
