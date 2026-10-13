using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.Tasks;

/// <summary>TaskCreate inserts a task and returns the row persisted by the database.</summary>
public sealed class TaskCreate : IRepoCommand<TaskParameters, TaskRow>
{
    /// <summary>Creates the command for the supplied task values.</summary>
    public TaskCreate(TaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, TaskRow> Key => TaskOperations.Create;

    /// <inheritdoc />
    public TaskParameters Parameters { get; }

    async Task<Result<TaskRow>> IRepoCommand<TaskParameters, TaskRow>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = TaskBinding.From(Parameters, cancellationToken);
            var task = await connection.QuerySingleAsync<TaskRow>(
                "INSERT INTO f002_tasks (id, title, note) VALUES (@Id, @Title, @Note) RETURNING id, title, note",
                binding, transaction: transaction, commandTimeout: 30);
            return Result<TaskRow>.Success(task);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Result<TaskRow>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }
}
