using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;

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

    async Task<TaskRow> IRepoCommand<TaskParameters, TaskRow>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var rows = (await connection.QueryAsync<TaskRow>(
            "SELECT 'invalid-integer' AS id, @Title AS title, @Note AS note", binding, transaction: transaction, commandTimeout: 30)).ToList();
        return rows[0];
    }
}
