namespace NyssaAI.Configuration.Contracts;

/// <summary>The outcome of removing a configuration value.</summary>
/// <remarks>
/// A successful configuration removal deletes the local entry. The provider reports whether it
/// still holds a remote copy, including a recoverable copy retained by soft deletion.
/// </remarks>
public sealed record ConfigurationRemoval
{
    /// <summary>Gets a value indicating whether the provider still holds the removed value remotely.</summary>
    /// <remarks>
    /// True includes providers that retain or soft-delete the value. False means the provider
    /// reports no remaining copy, or the entry used only local storage. Removing an already absent
    /// local entry also returns false: no provider is contacted and previously untracked remote
    /// copies are not inspected.
    /// </remarks>
    public required bool RemoteValueRemains { get; init; }
}
