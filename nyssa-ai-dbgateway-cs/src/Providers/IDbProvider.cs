using System.Data.Common;

namespace NyssaAI.DbGateway.Providers;

/// <summary>IDbProvider opens independent connections for one configured database dialect and engine.</summary>
public interface IDbProvider : IAsyncDisposable
{
    /// <summary>Gets the canonical lowercase provider name.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Gets the SQL dialect executed by this provider.</summary>
    public DbDialect Dialect { get; }

    /// <summary>Opens a fresh connection owned by the caller.</summary>
    /// <param name="cancellationToken">Cancels opening the connection.</param>
    /// <returns>An open <see cref="DbConnection"/> that the caller must dispose.</returns>
    public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
