using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Records the completed task moves in the shared transaction.</summary>
public sealed class TaskTransferRecord : IRepoCommand<TaskTransferSummary, TaskTransferSummary>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskTransferRecord(TaskTransferSummary parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskTransferSummary, TaskTransferSummary> Key => ProjectTaskOperations.RecordTransfer;

    /// <inheritdoc />
    public TaskTransferSummary Parameters { get; }

    async Task<TaskTransferSummary> IRepoCommand<TaskTransferSummary, TaskTransferSummary>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = new Binding
        {
            TransferId = Parameters.TransferId,
            SourceProjectId = Parameters.SourceProjectId,
            DestinationProjectId = Parameters.DestinationProjectId,
            TasksMoved = Parameters.TasksMoved,
            CancellationToken = cancellationToken
        };
        var value = await connection.QuerySingleAsync<TaskTransferSummary>(
            "INSERT INTO tutorial_task_transfers (transfer_id, source_project_id, destination_project_id, tasks_moved) VALUES (@TransferId, @SourceProjectId, @DestinationProjectId, @TasksMoved) RETURNING transfer_id AS TransferId, source_project_id AS SourceProjectId, destination_project_id AS DestinationProjectId, tasks_moved AS TasksMoved",
            binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
        return value;
    }
    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long TransferId { get; init; }
        public required long SourceProjectId { get; init; }
        public required long DestinationProjectId { get; init; }
        public required long TasksMoved { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
