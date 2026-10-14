using System.Data.Common;

namespace NyssaAI.Repository.Providers;

/// <summary>Owns a selected database provider and opens independent caller-owned connections.</summary>
/// <remarks>Finish and dispose all connections before disposing the provider. Implementations support concurrent opening and reject opening after disposal.</remarks>
public interface IRepositoryProvider : IAsyncDisposable
{
    /// <summary>Gets the canonical provider identity used by later command and migration selection.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Returns a new already-open connection. Failures throw after releasing partially acquired resources.</summary>
    /// <param name="cancellationToken">Cancels opening; cancellation throws OperationCanceledException.</param>
    /// <returns>An open connection owned by the caller.</returns>
    /// <remarks>Driver exceptions propagate unchanged. Cancellation responsiveness depends on the driver; Npgsql startup reads may end with their original timeout even after caller cancellation.</remarks>
    /// <exception cref="ObjectDisposedException">The provider is disposed.</exception>
    /// <exception cref="OperationCanceledException">Opening is canceled; implementations must preserve the caller's token.</exception>
    public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
