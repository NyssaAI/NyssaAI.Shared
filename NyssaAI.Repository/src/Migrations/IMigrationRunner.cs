namespace NyssaAI.Repository.Migrations;

/// <summary>Matches the database schema to an exact compiled application release pin.</summary>
/// <remarks>Invoke explicitly before serving traffic. Lock contention fails without applying migrations; failures attempt rollback. Resolve the cause and inspect database state before retrying an uncertain commit.</remarks>
public interface IMigrationRunner
{
    /// <summary>Atomically moves schema and history to the requested full semantic-version pin.</summary>
    /// <param name="release">Exact case-sensitive SemVer, including prerelease and build metadata.</param>
    /// <param name="cancellationToken">Cancels acquisition and execution; cleanup still runs.</param>
    /// <returns>The completed migration report directly.</returns>
    /// <exception cref="InvalidOperationException">The catalog is missing, history is corrupt, the lock is unavailable, or downgrade is forbidden.</exception>
    /// <remarks>Schema and history share one transaction, and failure attempts rollback. Database exceptions propagate unchanged; loss of the connection during commit can leave its outcome uncertain.</remarks>
    /// <exception cref="ArgumentException">The release is not a valid full semantic version.</exception>
    /// <exception cref="KeyNotFoundException">No exact release pin exists.</exception>
    /// <exception cref="NotSupportedException">The provider does not support transactional migrations.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before or during migration. Driver cancellation responsiveness and exceptions apply during database calls.</exception>
    public Task<MigrationReport> MigrateAsync(string release, CancellationToken cancellationToken = default);
}
