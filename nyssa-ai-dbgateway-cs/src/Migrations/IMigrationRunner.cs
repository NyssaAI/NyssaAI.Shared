namespace NyssaAI.DbGateway.Migrations;

/// <summary>IMigrationRunner applies or rolls back versioned schema migrations for the configured database provider.</summary>
public interface IMigrationRunner
{
    /// <summary>Migrates the database schema to the step pinned for the specified exact semantic version release.</summary>
    /// <param name="release">The exact full SemVer release identifier registered in the catalog.</param>
    /// <param name="cancellationToken">Cancels migration execution.</param>
    /// <returns>A report summarizing the starting step, ending step, and number of steps applied.</returns>
    /// <exception cref="ArgumentException">The release identifier is null, empty, or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">The release identifier is not registered in the catalog.</exception>
    /// <exception cref="InvalidOperationException">No catalog is registered, a lock is held, history is invalid, or a downgrade is irreversible.</exception>
    /// <exception cref="NotSupportedException">The provider does not support verified transactional migrations.</exception>
    /// <exception cref="OperationCanceledException">Execution is canceled.</exception>
    public Task<MigrationReport> MigrateAsync(string release, CancellationToken cancellationToken = default);
}
