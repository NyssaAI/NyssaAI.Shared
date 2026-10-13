using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.Tasks;

/// <summary>TaskMalformed exercises a generated row materialization failure.</summary>
public sealed class TaskMalformed : IRepoCommand<TaskParameters, TaskRow>
{
    /// <summary>Creates a command carrying the supplied fixture values.</summary>
    public TaskMalformed(TaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, TaskRow> Key => TaskOperations.Malformed;

    /// <inheritdoc />
    public TaskParameters Parameters { get; }

    async Task<Result<TaskRow>> IRepoCommand<TaskParameters, TaskRow>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        try
        {
            var rows = (await connection.QueryAsync<TaskRow>(
                "SELECT 'invalid-integer' AS id, @Title AS title, @Note AS note", binding, transaction: transaction, commandTimeout: 30)).ToList();
            return Result<TaskRow>.Success(rows[0]);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            // Generated readers can throw conversion errors directly; constrain translation to this database read.
            return Result<TaskRow>.Failure(Error.FromMessage("The task row has invalid database values.", ErrorCategory.Validation));
        }
    }
}
