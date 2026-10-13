using NyssaAI.Results;

namespace NyssaAI.Repository.Migrations;

/// <summary>Matches the database schema to an exact compiled application release pin.</summary>
/// <remarks>Invoke explicitly before serving traffic. Concurrent attempts fail; failures roll back and may be retried.</remarks>
public interface IMigrationRunner
{
    /// <summary>Atomically moves schema and history to the requested full semantic-version pin.</summary>
    /// <param name="release">Exact case-sensitive SemVer, including prerelease and build metadata.</param>
    /// <param name="cancellationToken">Cancels acquisition and execution; cleanup still runs.</param>
    public Task<Result<MigrationReport>> MigrateAsync(string release, CancellationToken cancellationToken = default);
}
