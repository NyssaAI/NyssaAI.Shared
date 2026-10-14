using NyssaAI.Repository.Client;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Migrations;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.Configuration;

/// <summary>RepositoryServiceCollectionExtensions simplifies library configuration through three routes:
/// an explicit immutable DTO, a developer-supplied configuration factory, or an already-open
/// NyssaAI.Configuration store and logical key.</summary>
/// <remarks>
/// Follows the IServiceCollection Add{Service} extension-method registration pattern.
/// The NyssaAI.Configuration variation asynchronously loads one DTO from an already-open store and key,
/// rather than binding a configuration section or interpreting a filesystem path. Generated JSON metadata
/// keeps that load compatible with Native AOT. Initialization failures, including loading and
/// cancellation failures, throw exceptions at these registration boundaries.
/// The host owns directory selection, store composition, and protection.
/// The DTO overload is the master registration method; other overloads load settings and delegate to it.
/// </remarks>
public static partial class RepositoryServiceCollectionExtensions
{
    /// <summary>Validates configuration and registers one lazily constructed singleton per service container.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="settings">Already-loaded settings; this method performs no configuration reads.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="commands">Explicit compiled command registrations; factories run only during resolution.</param>
    /// <param name="migrations">Compiled migration catalogs, one per built-in provider; invocation remains explicit.</param>
    /// <remarks>Register once per collection. Duplicate provider service registration fails without mutation. Dispose the container asynchronously after all connections finish.</remarks>
    /// <exception cref="ArgumentException">A required argument or settings value is invalid.</exception>
    /// <exception cref="InvalidOperationException">Registration is duplicated, the provider is missing, or a factory returns invalid output.</exception>
    /// <remarks>Configuration, preparation, and application callback exceptions propagate unchanged.</remarks>
    public static IServiceCollection AddNyssaRepository(this IServiceCollection services, RepositoryConfiguration settings, IReadOnlyList<RepositoryProviderRegistration>? providers = null, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(IRepositoryProvider) || service.ServiceType == typeof(IRepository) || service.ServiceType == typeof(IMigrationRunner)))
            throw new InvalidOperationException("A repository provider is already registered.");

        // 2. Validate built-in and custom registrations, then prepare the configured provider's factory.
        var createProvider = RepositoryProviderRegistry.RegisterDatabaseProviders(settings,
        [
            new() { DatabaseProvider = "sqlite", Prepare = RepositoryProviderSqlite.Prepare },
            new() { DatabaseProvider = "postgresql", Prepare = RepositoryProviderPostgres.Prepare },
            .. providers ?? []
        ]);

        // 3. Snapshot registrations before changing the collection.
        var commandCatalog = ReadCommands(services).Append(commands?.ToArray() ?? [], settings.DatabaseProvider);
        var catalogs = migrations?.ToArray() ?? [];
        if (catalogs.Any(catalog => catalog is null) || catalogs.Select(catalog => catalog.DatabaseProvider).Distinct(StringComparer.Ordinal).Count() != catalogs.Length)
            throw new ArgumentException("Migration catalogs require unique providers and cannot contain null.", nameof(migrations));
        // Let DI construct and own the provider when it is first resolved.
        InstallCommands(services, commandCatalog);
        services.AddSingleton<IRepositoryProvider>(_ => createProvider());
        services.AddSingleton<IRepository>(container =>
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            return new RepositoryClient(provider, new RepoCommandRegistry(provider.DatabaseProvider, container.GetRequiredService<RepositoryCommandCatalog>().Resolve()));
        });

        services.AddSingleton<IMigrationRunner>(container =>
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            return new MigrationRunner(provider, catalogs.SingleOrDefault(catalog => string.Equals(catalog.DatabaseProvider, provider.DatabaseProvider, StringComparison.Ordinal)));
        });
        return services;
    }

    /// <summary>Creates immutable settings with a callback, then delegates to the DTO registration method.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">A callback invoked once to return the complete immutable settings DTO.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="commands">Explicit compiled command registrations.</param>
    /// <param name="migrations">Compiled migration catalogs; invocation remains explicit.</param>
    /// <returns>The supplied service collection after registration.</returns>
    /// <exception cref="ArgumentException">A required argument or settings value is invalid.</exception>
    /// <exception cref="InvalidOperationException">Registration is duplicated, the provider is missing, or a factory returns invalid output.</exception>
    /// <remarks>Configuration, preparation, and application callback exceptions propagate unchanged.</remarks>
    public static IServiceCollection AddNyssaRepository(this IServiceCollection services, Func<RepositoryConfiguration> configure, IReadOnlyList<RepositoryProviderRegistration>? providers = null, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var settings = configure() ?? throw new InvalidOperationException("The configuration factory returned no settings.");
        return services.AddNyssaRepository(settings, providers, commands, migrations);
    }

    /// <summary>Loads stored repository settings asynchronously, then registers the validated snapshot.</summary>
    /// <param name="services">The host's service collection, before its final service provider is built.</param>
    /// <param name="configuration">An already-open NyssaAI.Configuration store, borrowed only for this call.</param>
    /// <param name="key">The settings key within nyssa.repository; not a directory, filename, or section path.</param>
    /// <param name="providers">Optional additional compiled provider registrations, as in the DTO overload.</param>
    /// <param name="cancellationToken">Cancels configuration loading and is checked again before registration.</param>
    /// <param name="commands">Explicit compiled command registrations.</param>
    /// <param name="migrations">Compiled migration catalogs; invocation remains explicit.</param>
    /// <returns>The supplied service collection after registration.</returns>
    /// <exception cref="ArgumentException">A required argument or settings value is invalid.</exception>
    /// <exception cref="InvalidOperationException">Registration is duplicated, the provider is missing, or a factory returns invalid output.</exception>
    /// <remarks>Configuration, preparation, and application callback exceptions propagate unchanged.</remarks>
    /// <remarks>
    /// Await before building the host's service provider; concurrent changes to the collection are not supported.
    /// Uses GetRepositoryConfigurationAsync followed by AddNyssaRepository(settings, providers).
    /// It does not select a directory, provision settings, open a database, or build a temporary service provider.
    /// The registered singleton retains only the prepared settings, not the borrowed configuration store.
    /// Stored changes affect future registrations only. Failures leave the collection unchanged.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Loading or registration is canceled.</exception>
    public static async Task<IServiceCollection> AddNyssaRepositoryAsync(this IServiceCollection services, ConfigurationStore configuration, string key = "default", IReadOnlyList<RepositoryProviderRegistration>? providers = null, CancellationToken cancellationToken = default, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        // 1. Validate the borrowed store and key before reading configuration.
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        cancellationToken.ThrowIfCancellationRequested();
        // 2. Load without provisioning defaults; loading errors stop initialization.
        var settings = await configuration.GetRepositoryConfigurationAsync(key, cancellationToken).ConfigureAwait(false);

        // 3. Check cancellation again in case it arrived during a successful read, then delegate.
        cancellationToken.ThrowIfCancellationRequested();
        return services.AddNyssaRepository(settings, providers, commands, migrations);
    }
}
