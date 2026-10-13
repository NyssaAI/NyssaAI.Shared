using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Inserts an open task and returns its persisted details.</summary>
public sealed class TaskInsert : IRepoCommand<TaskCreateParameters, TaskDetails>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskInsert(TaskCreateParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskCreateParameters, TaskDetails> Key => ProjectTaskOperations.Create;

    /// <inheritdoc />
    public TaskCreateParameters Parameters { get; }

    async Task<Result<TaskDetails>> IRepoCommand<TaskCreateParameters, TaskDetails>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                Id = Parameters.Id,
                ProjectId = Parameters.ProjectId,
                Title = Parameters.Title,
                Note = Parameters.Note,
                CancellationToken = cancellationToken
            };
            var value = await connection.QuerySingleAsync<TaskDetails>(
                "INSERT INTO tutorial_tasks (id, project_id, title, note, is_completed) VALUES (@Id, @ProjectId, @Title, @Note, FALSE) RETURNING id AS Id, project_id AS ProjectId, title AS Title, note AS Note, is_completed AS IsCompleted",
                binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
            return Result<TaskDetails>.Success(value);
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
        public required long ProjectId { get; init; }
        public required string Title { get; init; }
        public required string? Note { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
