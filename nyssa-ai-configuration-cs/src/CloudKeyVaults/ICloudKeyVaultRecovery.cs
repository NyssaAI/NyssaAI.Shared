using NyssaAI.Results;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>Optional capability implemented alongside Contracts.ICloudKeyVault for guarded, caller-driven recovery.</summary>
/// <remarks>
/// Preparation and reconciliation are read-only. Execute must atomically enforce the prepared
/// concurrency condition and deduplicate the original OperationId against its identical intent.
/// Reconciliation must verify that an applied output still identifies the original value; matching
/// content is insufficient. An expired request record or unavailable conditional mutation requires
/// Manual, never an unguarded retry. All returned errors and TargetReference values must be secret-free.
/// Implementations must preserve these guarantees across process restarts and concurrent callers.
/// </remarks>
public interface ICloudKeyVaultRecovery
{
    /// <summary>The actual backend identity, including account, tenant and store as applicable; never credentials. Must remain stable for the adapter's lifetime.</summary>
    string StoreIdentity { get; }
    /// <summary>Captures the target identity and atomic concurrency condition without changing remote state.</summary>
    ValueTask<Result<CloudKeyVaultPreparation>> PrepareAsync(CloudKeyVaultOperation operation, CancellationToken cancellationToken = default);
    /// <summary>Executes the prepared request once, recognizing repeat IDs before testing an already-consumed condition.</summary>
    ValueTask<Result<CloudKeyVaultEffect>> ExecuteAsync(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation, CancellationToken cancellationToken = default);
    /// <summary>Establishes the original request's effect and whether its output or pending precondition is still valid, without mutation.</summary>
    ValueTask<Result<CloudKeyVaultEffect>> ReconcileAsync(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation, CancellationToken cancellationToken = default);
}
