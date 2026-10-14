using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.Configuration;

public static partial class RepositoryServiceCollectionExtensions
{
    /// <summary>Registers commands independently, before or after repository setup and before building the container.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="commands">Declares commands once without executing their factories.</param>
    /// <returns>The supplied collection. Invalid or duplicate registrations throw before changing it.</returns>
    public static IServiceCollection AddNyssaRepositoryCommands(this IServiceCollection services, Action<RepositoryCommandBuilder> commands)
    {
        ArgumentNullException.ThrowIfNull(services);
        var catalog = ReadCommands(services).Append(CollectCommands(services, commands));
        InstallCommands(services, catalog);
        return services;
    }

    /// <summary>Registers a repository with inline typed command declarations.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="settings">The selected database and connection settings.</param>
    /// <param name="commands">Declares commands; omitted providers use the selected database.</param>
    /// <param name="providers">Optional application provider registrations.</param>
    /// <param name="migrations">Optional compiled migration catalogs.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddNyssaRepository(this IServiceCollection services, RepositoryConfiguration settings,
        Action<RepositoryCommandBuilder> commands, IReadOnlyList<RepositoryProviderRegistration>? providers = null,
        IReadOnlyList<MigrationCatalog>? migrations = null) =>
        services.AddNyssaRepository(settings, providers, CollectCommands(services, commands), migrations);

    /// <summary>Creates settings once and registers inline commands through the common registration path.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">Creates the settings snapshot.</param>
    /// <param name="commands">Declares typed commands.</param>
    /// <param name="providers">Optional application providers.</param>
    /// <param name="migrations">Optional migration catalogs.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddNyssaRepository(this IServiceCollection services, Func<RepositoryConfiguration> configure,
        Action<RepositoryCommandBuilder> commands, IReadOnlyList<RepositoryProviderRegistration>? providers = null,
        IReadOnlyList<MigrationCatalog>? migrations = null) =>
        services.AddNyssaRepository(configure, providers, CollectCommands(services, commands), migrations);

    /// <summary>Loads stored settings and registers inline commands without opening a database.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">The borrowed configuration store.</param>
    /// <param name="commands">Declares typed commands.</param>
    /// <param name="key">The stored settings key.</param>
    /// <param name="providers">Optional application providers.</param>
    /// <param name="cancellationToken">Cancels loading and registration.</param>
    /// <param name="migrations">Optional migration catalogs.</param>
    /// <returns>The supplied service collection.</returns>
    public static async Task<IServiceCollection> AddNyssaRepositoryAsync(this IServiceCollection services, ConfigurationStore configuration,
        Action<RepositoryCommandBuilder> commands, string key = "default", IReadOnlyList<RepositoryProviderRegistration>? providers = null,
        CancellationToken cancellationToken = default, IReadOnlyList<MigrationCatalog>? migrations = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registrations = CollectCommands(services, commands);
        return await services.AddNyssaRepositoryAsync(configuration, key, providers, cancellationToken, registrations, migrations).ConfigureAwait(false);
    }

    private static RepoCommandRegistration[] CollectCommands(IServiceCollection services, Action<RepositoryCommandBuilder> commands)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commands);
        var builder = new RepositoryCommandBuilder();
        commands(builder);
        return builder.Snapshot();
    }

    private static RepositoryCommandCatalog ReadCommands(IServiceCollection services) =>
        services.SingleOrDefault(entry => entry.ServiceType == typeof(RepositoryCommandCatalog))?.ImplementationInstance as RepositoryCommandCatalog
        ?? new RepositoryCommandCatalog { Registrations = [] };

    private static void InstallCommands(IServiceCollection services, RepositoryCommandCatalog catalog)
    {
        var previous = services.SingleOrDefault(entry => entry.ServiceType == typeof(RepositoryCommandCatalog));
        if (previous is not null) services.Remove(previous);
        services.AddSingleton(catalog);
    }
}
