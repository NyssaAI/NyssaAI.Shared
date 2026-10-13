using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

namespace NyssaAI.Repository.Providers;

/// <summary>Validates all registrations and returns the selected provider's construction factory.</summary>
internal static class RepositoryProviderRegistry
{
    /// <summary>
    /// Validates the settings and all provider registrations, then returns a function
    /// that constructs the configured database provider when invoked.
    /// </summary>
    /// <param name="settings">The database provider name and connection settings to prepare.</param>
    /// <param name="registrations">The available compiled provider registrations; normalized names must be unique.</param>
    /// <returns>
    /// A successful result containing the selected provider's construction factory,
    /// or a failure when registration validation, provider selection, or preparation fails.
    /// </returns>
    /// <exception cref="ResultException">Settings or registrations are null, or settings validation fails.</exception>
    /// <remarks>
    /// Only the selected registration is prepared, using a copy of the settings.
    /// This method does not invoke the returned factory, construct a provider, or open a connection.
    /// The caller can pass the factory to dependency injection for lazy provider construction.
    /// </remarks>
    public static Result<Func<IRepositoryProvider>> RegisterDatabaseProviders(RepositoryConfiguration settings,IReadOnlyList<RepositoryProviderRegistration> registrations)
    {
        // Check inputs in sequence, stopping at the first failure.
        Guard.Against.Null(settings, nameof(settings))
            .Then(validatedSettings => Guard.Against.Null(registrations, nameof(registrations))
                .Then(_ => validatedSettings.Validate().ToGeneric(() => validatedSettings))).ThrowIfError();

        // Validate the entire list before preparing the selected provider; no provider instances are created here.
        var registry = new Dictionary<string, RepositoryProviderRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            var complete = Guard.Against.False(
                registration,
                entry => entry is not null
                    && !string.IsNullOrWhiteSpace(entry.DatabaseProvider)
                    && entry.Prepare is not null,
                "A provider registration is incomplete.");

            if (complete.IsFailure)
                return Result<Func<IRepositoryProvider>>.Failure(complete.Errors);

            // Normalize names so values such as "sqlite" and " SQLITE " conflict.
            var unique = Guard.Against.False(
                registry.TryAdd(complete.Value.DatabaseProvider.Trim().ToLowerInvariant(), complete.Value),
                "Database provider registrations must have unique names.",
                category: ErrorCategory.Conflict);

            if (unique.IsFailure)
                return Result<Func<IRepositoryProvider>>.Failure(unique.Errors);
        }

        var registered = Guard.Against.False(
            registry.ContainsKey(settings.DatabaseProvider),
            "The configured database provider is not registered.");

        if (registered.IsFailure)
            return Result<Func<IRepositoryProvider>>.Failure(registered.Errors);

        // Preparation validates provider-specific settings; the returned factory is not invoked here.
        var prepared = registry[settings.DatabaseProvider].Prepare(settings with { });
        // A returned failure is allowed through so its errors can be sanitized below.
        var factory = Guard.Against.False(
            prepared,
            result => result is not null && (result.IsFailure || result.Value is not null),
            "The database provider returned no construction factory.",
            category: ErrorCategory.Unexpected);

        if (factory.IsFailure)
            return Result<Func<IRepositoryProvider>>.Failure(factory.Errors);

        // Hide connection details while preserving each error category.
        if (prepared.IsFailure)
        {
            return Result<Func<IRepositoryProvider>>.Failure(prepared.Errors.Select(error =>
                Error.FromMessage("Database provider settings could not be prepared.", error.Category)));
        }

        return prepared;
    }
}
