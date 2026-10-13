using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.Tasks;

/// <summary>TaskOutboxWrite persists a notification in the same transaction as its task.</summary>
public sealed class TaskOutboxWrite : IRepoCommand<TaskParameters, int>
{
    /// <summary>Creates the command for the supplied task data.</summary>
    public TaskOutboxWrite(TaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, int> Key => TaskOperations.Outbox;

    /// <inheritdoc />
    public TaskParameters Parameters { get; }

    async Task<Result<int>> IRepoCommand<TaskParameters, int>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var count = await connection.ExecuteAsync("INSERT INTO f002_outbox (task_id, title) VALUES (@Id, @Title)", binding, transaction: transaction, commandTimeout: 30);
        return Result<int>.Success(count);
    }
}
