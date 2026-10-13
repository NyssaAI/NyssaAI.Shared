namespace NyssaAI.Repository.Commands;

/// <summary>RepoCommandKey identifies an operation with fixed parameter and complete successful result types.</summary>
/// <typeparam name="TParameters">The input contract shared by the operation's provider implementations.</typeparam>
/// <typeparam name="TResult">The complete successful result, not necessarily a database row type.</typeparam>
/// <remarks>
/// Providers register the same logical name and type contracts for equivalent operations.
/// Registry resolution validates the name and registration; creating a key performs no I/O.
/// Record equality includes the closed generic type and uses ordinal, case-sensitive name equality.
/// </remarks>
public sealed record RepoCommandKey<TParameters, TResult>
{
    /// <summary>Gets the logical operation name shared by its provider-specific registrations.</summary>
    public required string Name { get; init; }
}
