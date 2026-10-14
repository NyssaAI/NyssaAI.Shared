using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Aggregates the completed and remaining task counts for a project.</summary>
public sealed class ProjectProgressGet : IRepoCommand<ProjectTaskParameters, ProjectProgress>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public ProjectProgressGet(ProjectTaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<ProjectTaskParameters, ProjectProgress> Key => ProjectTaskOperations.Progress;

    /// <inheritdoc />
    public ProjectTaskParameters Parameters { get; }

    async Task<ProjectProgress> IRepoCommand<ProjectTaskParameters, ProjectProgress>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = new Binding
        {
            ProjectId = Parameters.ProjectId,
            CancellationToken = cancellationToken
        };
        var value = await connection.QuerySingleAsync<ProjectProgress>(
            "SELECT @ProjectId AS ProjectId, COUNT(*) AS TotalTasks, COALESCE(SUM(CASE WHEN is_completed = TRUE THEN 1 ELSE 0 END), 0) AS CompletedTasks, COALESCE(SUM(CASE WHEN is_completed = FALSE THEN 1 ELSE 0 END), 0) AS RemainingTasks FROM tutorial_tasks WHERE project_id = @ProjectId",
            binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
        return value;
    }
    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long ProjectId { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
