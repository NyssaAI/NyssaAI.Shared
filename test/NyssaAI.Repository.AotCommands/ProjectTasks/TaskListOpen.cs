using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Returns the open tasks in a project, ordered by identifier.</summary>
public sealed class TaskListOpen : IRepoCommand<ProjectTaskParameters, IReadOnlyList<TaskSummary>>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskListOpen(ProjectTaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<ProjectTaskParameters, IReadOnlyList<TaskSummary>> Key => ProjectTaskOperations.ListOpen;

    /// <inheritdoc />
    public ProjectTaskParameters Parameters { get; }

    async Task<Result<IReadOnlyList<TaskSummary>>> IRepoCommand<ProjectTaskParameters, IReadOnlyList<TaskSummary>>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                ProjectId = Parameters.ProjectId,
                CancellationToken = cancellationToken
            };
            var tasks = await connection.QueryAsync<TaskSummary>(
                "SELECT id AS Id, title AS Title FROM tutorial_tasks WHERE project_id = @ProjectId AND is_completed = FALSE ORDER BY id",
                binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
            return Result<IReadOnlyList<TaskSummary>>.Success(tasks.ToList());
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Result<IReadOnlyList<TaskSummary>>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }

    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long ProjectId { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
