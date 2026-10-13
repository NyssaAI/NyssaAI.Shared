using NyssaAI.Repository.Client;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Providers;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Migrations;
using NyssaAI.Results;
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
/// cancellation failures, explicitly throw ResultException at these registration boundaries.
/// The host owns directory selection, store composition, and protection.
/// The DTO overload is the master registration method; other overloads load settings and delegate to it.
/// </remarks>
public static class RepositoryServiceCollectionExtensions
{
    /// <summary>Validates configuration and registers one lazily constructed singleton per service container.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="settings">Already-loaded settings; this method performs no configuration reads.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="commands">Explicit compiled command registrations; factories run only during resolution.</param>
    /// <param name="migrations">Compiled migration catalogs, one per built-in provider; invocation remains explicit.</param>
    /// <remarks>Register once per collection. Duplicate provider service registration fails without mutation. Dispose the container asynchronously after all connections finish.</remarks>
    /// <exception cref="ResultException">Registration preconditions or provider preparation fail.</exception>
    public static Result AddNyssaRepository(this IServiceCollection services, RepositoryConfiguration settings, IReadOnlyList<RepositoryProviderRegistration>? providers = null, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        // 1. Require a service collection without an existing repository provider registration.
        Guard.Against.Contains(services,
                               (ServiceDescriptor service) => service.ServiceType == typeof(IRepositoryProvider) || service.ServiceType == typeof(IRepository) || service.ServiceType == typeof(IMigrationRunner),
                               "Unable to register the data repository",
                               "A repository provider is already registered.",
                               ErrorCategory.Conflict).ThrowIfError();

        // 2. Validate built-in and custom registrations, then prepare the configured provider's factory.
        var createProvider = RepositoryProviderRegistry.RegisterDatabaseProviders(settings,
        [
            new() { DatabaseProvider = "sqlite", Prepare = RepositoryProviderSqlite.Prepare },
            new() { DatabaseProvider = "postgresql", Prepare = RepositoryProviderPostgres.Prepare },
            .. providers ?? []
        ]).ThrowIfError();

        // 3. Snapshot registrations before changing the collection.
        var registrations = commands?.ToArray() ?? [];
        Guard.Against.True(registrations.Any(command => command is null), "Command registrations cannot contain null.", category: ErrorCategory.Validation).ToGeneric(() => registrations).ThrowIfError();
        var catalogs = migrations?.ToArray() ?? [];
        Guard.Against.True(catalogs.Any(catalog => catalog is null) || catalogs.Select(catalog => catalog.DatabaseProvider).Distinct(StringComparer.Ordinal).Count() != catalogs.Length,
            "Migration catalogs require unique providers and cannot contain null.", category: ErrorCategory.Validation).ToGeneric(() => catalogs).ThrowIfError();
        // Let DI construct and own the provider when it is first resolved.
        services.AddSingleton<IRepositoryProvider>(_ => createProvider());
        services.AddSingleton<IRepository>(container =>
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            return new RepositoryClient(provider, new RepoCommandRegistry(provider.DatabaseProvider, registrations));
        });

        services.AddSingleton<IMigrationRunner>(container =>
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            return new MigrationRunner(provider, catalogs.SingleOrDefault(catalog => string.Equals(catalog.DatabaseProvider, provider.DatabaseProvider, StringComparison.Ordinal)));
        });
        return Result.Success();
    }

    /// <summary>Creates immutable settings with a callback, then delegates to the DTO registration method.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">A callback invoked once to return the complete immutable settings DTO.</param>
    /// <param name="providers">Optional additional compiled provider registrations.</param>
    /// <param name="commands">Explicit compiled command registrations.</param>
    /// <param name="migrations">Compiled migration catalogs; invocation remains explicit.</param>
    /// <returns>Success after the master method registers the provider factory.</returns>
    /// <exception cref="ResultException">Input validation, the callback, or provider preparation fails.</exception>
    public static Result AddNyssaRepository(this IServiceCollection services, Func<RepositoryConfiguration> configure, IReadOnlyList<RepositoryProviderRegistration>? providers = null, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        // 1. Validate inputs before invoking application configuration code.
        var createSettings = Guard.Against.Null(services, nameof(services))
            .Then(_ => Guard.Against.Null(configure, nameof(configure))).ThrowIfError();

        // 2. Invoke the callback once. Do not expose potentially sensitive callback exception text.
        var configured = Result.Try(createSettings);
        if (configured.IsFailure)
        {
            configured = Result<RepositoryConfiguration>.Failure(configured.Errors.Select(error =>
                Error.FromMessage("Repository configuration could not be created.", error.Category)));
        }

        // 3. The master method owns settings validation, provider preparation, and DI registration.
        return services.AddNyssaRepository(configured.ThrowIfError(), providers, commands, migrations);
    }

    /// <summary>Loads stored repository settings asynchronously, then registers the validated snapshot.</summary>
    /// <param name="services">The host's service collection, before its final service provider is built.</param>
    /// <param name="configuration">An already-open NyssaAI.Configuration store, borrowed only for this call.</param>
    /// <param name="key">The settings key within nyssa.repository; not a directory, filename, or section path.</param>
    /// <param name="providers">Optional additional compiled provider registrations, as in the DTO overload.</param>
    /// <param name="cancellationToken">Cancels configuration loading and is checked again before registration.</param>
    /// <param name="commands">Explicit compiled command registrations.</param>
    /// <param name="migrations">Compiled migration catalogs; invocation remains explicit.</param>
    /// <returns>Success after the master method registers the provider factory.</returns>
    /// <exception cref="ResultException">Input validation, configuration loading, cancellation, or provider preparation fails.</exception>
    /// <remarks>
    /// Await before building the host's service provider; concurrent changes to the collection are not supported.
    /// Uses GetRepositoryConfigurationAsync followed by AddNyssaRepository(settings, providers).
    /// It does not select a directory, provision settings, open a database, or build a temporary service provider.
    /// The registered singleton retains only the prepared settings, not the borrowed configuration store.
    /// Stored changes affect future registrations only. Failures leave the collection unchanged.
    /// </remarks>
    public static async Task<Result> AddNyssaRepositoryAsync(this IServiceCollection services, ConfigurationStore configuration, string key = "default", IReadOnlyList<RepositoryProviderRegistration>? providers = null, CancellationToken cancellationToken = default, IReadOnlyList<RepoCommandRegistration>? commands = null, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        // 1. Validate the borrowed store and key before reading configuration.
        Guard.Against.Null(services, nameof(services))
            .Then(_ => Guard.Against.Null(configuration, nameof(configuration)))
            .Then(_ => Guard.Against.NullOrWhiteSpace(key, nameof(key)))
            .Then(_ => Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled).ToGeneric(() => key))
            .ThrowIfError();

        // 2. Load without provisioning defaults; loading errors stop initialization.
        var loaded = await configuration.GetRepositoryConfigurationAsync(key, cancellationToken).ConfigureAwait(false);
        var settings = loaded.ThrowIfError();

        // 3. Check cancellation again in case it arrived during a successful read, then delegate.
        Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled)
            .ToGeneric(() => settings).ThrowIfError();
        return services.AddNyssaRepository(settings, providers, commands, migrations);
    }
}
