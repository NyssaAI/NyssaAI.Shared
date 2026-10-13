using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Reads task details or returns NotFound.</summary>
public sealed class TaskGet : IRepoCommand<TaskIdParameters, TaskDetails>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskGet(TaskIdParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskIdParameters, TaskDetails> Key => ProjectTaskOperations.Get;

    /// <inheritdoc />
    public TaskIdParameters Parameters { get; }

    async Task<Result<TaskDetails>> IRepoCommand<TaskIdParameters, TaskDetails>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                Id = Parameters.Id,
                CancellationToken = cancellationToken
            };
            var task = await connection.QuerySingleOrDefaultAsync<TaskDetails>(
                "SELECT id AS Id, project_id AS ProjectId, title AS Title, note AS Note, is_completed AS IsCompleted FROM tutorial_tasks WHERE id = @Id",
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
        public required CancellationToken CancellationToken { get; init; }
    }
}
