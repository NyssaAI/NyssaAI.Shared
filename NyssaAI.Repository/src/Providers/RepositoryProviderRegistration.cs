using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

namespace NyssaAI.Repository.Providers;

/// <summary>Registers compiled provider preparation and construction, not another settings DTO.</summary>
public sealed record RepositoryProviderRegistration
{
    /// <summary>Gets the provider key; registration compares trimmed invariant-lowercase keys.</summary>
    public required string DatabaseProvider { get; init; }

    /// <summary>Validates settings without I/O or resource allocation and returns a factory capturing that snapshot.</summary>
    /// <remarks>
    /// The returned factory must be total, perform no I/O, and create a fresh provider on each invocation.
    /// Its provider identity must equal the normalized registration key. Resource acquisition and fallible
    /// initialization belong to OpenConnectionAsync. Preparation failures must not contain credentials.
    /// This split permits validation before DI registration without creating an unowned disposable instance.
    /// </remarks>
    public required Func<RepositoryConfiguration, Result<Func<IRepositoryProvider>>> Prepare { get; init; }

    /// <summary>Returns the type name without displaying custom registration details.</summary>
    public override string ToString() => nameof(RepositoryProviderRegistration);
}
