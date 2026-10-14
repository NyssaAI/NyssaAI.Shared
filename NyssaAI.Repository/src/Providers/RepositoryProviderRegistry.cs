using NyssaAI.Repository.Configuration;

namespace NyssaAI.Repository.Providers;

internal static class RepositoryProviderRegistry
{
    public static Func<IRepositoryProvider> RegisterDatabaseProviders(RepositoryConfiguration settings,
        IReadOnlyList<RepositoryProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registrations);
        settings.Validate();
        var registry = new Dictionary<string, RepositoryProviderRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (registration is null || string.IsNullOrWhiteSpace(registration.DatabaseProvider) || registration.Prepare is null)
                throw new ArgumentException("A provider registration is incomplete.", nameof(registrations));
            if (!registry.TryAdd(registration.DatabaseProvider.Trim().ToLowerInvariant(), registration))
                throw new InvalidOperationException("Database provider registrations must have unique names.");
        }
        if (!registry.TryGetValue(settings.DatabaseProvider, out var selected))
            throw new InvalidOperationException("The configured database provider is not registered.");
        var prepared = selected.Prepare(settings with { })
            ?? throw new InvalidOperationException("Database provider preparation returned no factory.");
        return () => prepared() ?? throw new InvalidOperationException("The provider factory returned no provider.");
    }
}
