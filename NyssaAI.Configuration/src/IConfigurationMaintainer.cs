using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;

namespace NyssaAI.Configuration;

/// <summary>Migrates configuration entries across providers and recovers interrupted multi-step operations.</summary>
public interface IConfigurationMaintainer
{
    /// <summary>Moves one value to a different provider, withdrawing the copy it leaves behind.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="toProvider">
    /// The destination provider, or null to return to default storage. Moving to the destination
    /// the value already uses is refused rather than treated as a no-op.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the move.</param>
    /// <returns>
    /// What the move did, including whether the source copy was withdrawn. A false
    /// <see cref="ConfigurationMove.SourceWithdrawn"/> means the value is safely readable at its new
    /// home but a copy survives at the old one, including after soft deletion; the same fact is logged as a warning.
    /// </returns>
    ValueTask<ConfigurationMove> MoveAsync(
        string @namespace,
        string key,
        string? toProvider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Continues unfinished move or namespace work captured by a recovery handle.</summary>
    /// <remarks>Validates the handle, checks for changed local and remote values, reconciles uncertain requests, and executes only safely unfinished work. Unsupported providers require manual reconciliation. Retention is terminal; this never purges or rolls back.</remarks>
    /// <param name="handle">A handle from an outcome or exception, optionally imported after restart.</param>
    /// <param name="cancellationToken">Cancels this attempt without discarding its progress.</param>
    ValueTask<ConfigurationRecoveryOutcome> RecoverAsync(
        ConfigurationRecoveryHandle handle,
        CancellationToken cancellationToken = default);
}
