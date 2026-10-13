using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Changes a task title and returns the updated details.</summary>
public sealed class TaskRename : IRepoCommand<TaskRenameParameters, TaskDetails>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskRename(TaskRenameParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskRenameParameters, TaskDetails> Key => ProjectTaskOperations.Rename;

    /// <inheritdoc />
    public TaskRenameParameters Parameters { get; }

    async Task<Result<TaskDetails>> IRepoCommand<TaskRenameParameters, TaskDetails>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                Id = Parameters.Id,
                Title = Parameters.Title,
                CancellationToken = cancellationToken
            };
            var task = await connection.QuerySingleOrDefaultAsync<TaskDetails>(
                "UPDATE tutorial_tasks SET title = @Title WHERE id = @Id RETURNING id AS Id, project_id AS ProjectId, title AS Title, note AS Note, is_completed AS IsCompleted",
                binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
            return task is null
                ? Result<TaskDetails>.Failure(Error.FromMessage("Task not found.", ErrorCategory.NotFound))
                : Result<TaskDetails>.Success(task);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Result<TaskDetails>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }

    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long Id { get; init; }
        public required string Title { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
