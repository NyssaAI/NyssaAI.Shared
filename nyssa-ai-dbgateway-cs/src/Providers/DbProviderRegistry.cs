using NyssaAI.DbGateway.Configuration;

namespace NyssaAI.DbGateway.Providers;

/// <summary>Validates provider registrations and prepares the selected provider factory.</summary>
internal static class DbProviderRegistry
{
    public static Func<IDbProvider> RegisterDatabaseProviders(
        DbGatewayConfiguration settings,
        IReadOnlyList<DbProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        ArgumentNullException.ThrowIfNull(registrations);
        if (registrations.Count == 0)
            throw new ArgumentException("At least one database provider registration is required.", nameof(registrations));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        DbProviderRegistration? matched = null;
        foreach (var registration in registrations)
        {
            if (registration is null)
                throw new ArgumentException("Provider registrations cannot contain null.", nameof(registrations));
            registration.Validate();
            if (!seen.Add(registration.DatabaseProvider))
                throw new InvalidOperationException($"Duplicate database provider registration: {registration.DatabaseProvider}.");
            if (string.Equals(registration.DatabaseProvider, settings.DatabaseProvider, StringComparison.Ordinal))
                matched = registration;
        }

        if (matched is null)
            throw new InvalidOperationException($"No database provider is registered for '{settings.DatabaseProvider}'.");

        var create = matched.Prepare(settings) ?? throw new InvalidOperationException("The provider preparation callback returned no factory.");
        return () =>
        {
            var provider = create() ?? throw new InvalidOperationException("The provider factory returned no provider.");
            if (!string.Equals(provider.DatabaseProvider?.Trim().ToLowerInvariant(), matched.DatabaseProvider, StringComparison.Ordinal))
                throw new InvalidOperationException("The constructed provider does not match its registered identity.");
            return provider;
        };
    }
}
