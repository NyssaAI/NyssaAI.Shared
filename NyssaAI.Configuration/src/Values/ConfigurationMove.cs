namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// What one completed move did: which value changed hands, the provider it left, the provider it
/// arrived at, and whether the old copy was withdrawn.
/// </summary>
/// <remarks>
/// <para>
/// A move spans two stores and no transaction covers both, so the order is chosen to never lose the
/// value: read from the source, write to the destination, publish the entry, and only then withdraw
/// the source. If the provider retains a copy or withdrawal fails, the value is safely readable at
/// its new home and a copy survives at the old one — which is why <see cref="SourceWithdrawn"/> exists rather than the move
/// reporting a failure it did not have. A caller that must guarantee no second copy checks this
/// flag; the same fact is logged as a warning.
/// </para>
/// <para>
/// The reverse order would risk the opposite: a withdrawal that succeeded followed by a publish that
/// did not would destroy the only copy. A stranded secret can be cleaned up, so it is the failure
/// worth preferring.
/// </para>
/// </remarks>
public sealed record ConfigurationMove
{
    /// <summary>Gets the namespace holding the value that moved.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the key of the value that moved.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the provider the value left, or an empty string for default storage.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Gets the provider the value arrived at, or an empty string for default storage.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source copy was withdrawn.
    /// </summary>
    /// <remarks>
    /// False means the move completed and the entry is correct, but a copy remains at
    /// <see cref="From"/>, including a recoverable soft-deleted copy. Cleanup depends on that
    /// provider's retention policy. It is never false for a provider that sealed the value into the
    /// entry, because overwriting the entry withdrew it.
    /// </remarks>
    public required bool SourceWithdrawn { get; init; }
    /// <summary>Detailed destination, publication, and source cleanup progress.</summary>
    public Recovery.ConfigurationOperationProgress? Progress { get; init; }

    /// <summary>Protected continuation state when cleanup remains safely recoverable.</summary>
    public Recovery.ConfigurationRecoveryHandle? RecoveryHandle { get; init; }

}
