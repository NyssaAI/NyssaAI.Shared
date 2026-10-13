using System.Data.Common;
using NyssaAI.Results;

namespace NyssaAI.Repository.Providers;

/// <summary>Owns a selected database provider and opens independent caller-owned connections.</summary>
/// <remarks>Finish and dispose all connections before disposing the provider. Implementations support concurrent opening and reject opening after disposal.</remarks>
public interface IRepositoryProvider : IAsyncDisposable
{
    /// <summary>Gets the canonical provider identity used by later command and migration selection.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Returns a new already-open connection, or a sanitized failure with partial resources released.</summary>
    /// <param name="cancellationToken">Cancels opening; cancellation is returned as a Canceled failure.</param>
    public Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
