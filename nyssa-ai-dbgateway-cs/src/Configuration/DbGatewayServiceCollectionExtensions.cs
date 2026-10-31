using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Gateway;
using NyssaAI.DbGateway.Migrations;
using NyssaAI.DbGateway.Providers;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.DbGateway.Configuration;

/// <summary>Registers <see cref="IDbGateway"/>, <see cref="IDbProvider"/>, and <see cref="IMigrationRunner"/> in a dependency injection container.</summary>
public static class DbGatewayServiceCollectionExtensions
{
    /// <summary>Validates configuration and registers one lazily constructed database gateway singleton per service container.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="settings">Already-loaded settings; this method performs no configuration reads or database I/O.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="migrations">Optional compiled migration catalog supporting universal and paired SQLite/PostgreSQL steps.</param>
    /// <returns>The supplied service collection after registration.</returns>
    /// <exception cref="ArgumentException">A required argument or settings value is invalid.</exception>
    /// <exception cref="InvalidOperationException">Registration is duplicated, the provider is missing, or a factory returns invalid output.</exception>
    public static IServiceCollection AddNyssaDbGateway(
        this IServiceCollection services,
        DbGatewayConfiguration settings,
        IReadOnlyList<DbProviderRegistration>? providers = null,
        MigrationCatalog? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        if (services.Any(service =>
            service.ServiceType == typeof(IDbProvider) ||
            service.ServiceType == typeof(IDbGateway) ||
            service.ServiceType == typeof(IMigrationRunner)))
        {
            throw new InvalidOperationException("A database gateway provider is already registered.");
        }

        var createProvider = DbProviderRegistry.RegisterDatabaseProviders(settings,
        [
            new() { DatabaseProvider = "sqlite", Prepare = DbProviderSqlite.Prepare },
            new() { DatabaseProvider = "postgresql", Prepare = DbProviderPostgres.Prepare },
            .. providers ?? []
        ]);

        var timeoutSeconds = settings.CommandTimeoutSeconds;
        services.AddSingleton<IDbProvider>(_ => createProvider());
        services.AddSingleton<IDbGateway>(container =>
        {
            var provider = container.GetRequiredService<IDbProvider>();
            return new DbGatewayClient(provider, timeoutSeconds);
        });
        services.AddSingleton<IMigrationRunner>(container =>
        {
            var provider = container.GetRequiredService<IDbProvider>();
            return new MigrationRunner(provider, migrations);
        });
        return services;
    }

    /// <summary>Creates immutable settings with a callback, then delegates to the DTO registration method.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">A callback invoked once to return the complete immutable settings DTO.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="migrations">Optional compiled migration catalog.</param>
    /// <returns>The supplied service collection after registration.</returns>
    public static IServiceCollection AddNyssaDbGateway(
        this IServiceCollection services,
        Func<DbGatewayConfiguration> configure,
        IReadOnlyList<DbProviderRegistration>? providers = null,
        MigrationCatalog? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var settings = configure() ?? throw new InvalidOperationException("The configuration factory returned no settings.");
        return services.AddNyssaDbGateway(settings, providers, migrations);
    }

    /// <summary>Loads stored database gateway settings asynchronously, then registers the validated snapshot.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">An already-open <see cref="ConfigurationStore"/> borrowed only for this call.</param>
    /// <param name="key">The settings key within <c>nyssa.dbgateway</c>.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="migrations">Optional compiled migration catalog.</param>
    /// <param name="cancellationToken">Cancels configuration loading and registration.</param>
    /// <returns>The supplied service collection after registration.</returns>
    public static async Task<IServiceCollection> AddNyssaDbGatewayAsync(
        this IServiceCollection services,
        ConfigurationStore configuration,
        string key = "default",
        IReadOnlyList<DbProviderRegistration>? providers = null,
        MigrationCatalog? migrations = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        cancellationToken.ThrowIfCancellationRequested();
        var settings = await configuration.GetDbGatewayConfigurationAsync(key, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return services.AddNyssaDbGateway(settings, providers, migrations);
    }
}
