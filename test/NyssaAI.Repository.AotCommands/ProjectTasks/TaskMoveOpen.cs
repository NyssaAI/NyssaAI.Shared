using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Moves all currently open tasks and reports the actual affected count.</summary>
public sealed class TaskMoveOpen : IRepoCommand<TaskTransferParameters, TaskTransferSummary>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskMoveOpen(TaskTransferParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskTransferParameters, TaskTransferSummary> Key => ProjectTaskOperations.MoveOpen;

    /// <inheritdoc />
    public TaskTransferParameters Parameters { get; }

    async Task<Result<TaskTransferSummary>> IRepoCommand<TaskTransferParameters, TaskTransferSummary>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                SourceProjectId = Parameters.SourceProjectId,
                DestinationProjectId = Parameters.DestinationProjectId,
                CancellationToken = cancellationToken
            };
            var moved = await connection.ExecuteAsync(
                "UPDATE tutorial_tasks SET project_id = @DestinationProjectId WHERE project_id = @SourceProjectId AND is_completed = FALSE",
                binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
            return Result<TaskTransferSummary>.Success(new TaskTransferSummary
            {
                TransferId = Parameters.TransferId,
                SourceProjectId = Parameters.SourceProjectId,
                DestinationProjectId = Parameters.DestinationProjectId,
                TasksMoved = moved
            });
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Result<TaskTransferSummary>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }

    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long SourceProjectId { get; init; }
        public required long DestinationProjectId { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
