using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskReportQuery Combines two supported concrete calls inside the same borrowed transaction.</summary>
public sealed class TaskReportQuery : IRepoCommand<TaskParameters, TaskReport>
{
    /// <summary>Creates the command.</summary>
    public TaskReportQuery(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, TaskReport> Key => TaskOperations.Report;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<TaskReport> IRepoCommand<TaskParameters, TaskReport>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM f002_tasks WHERE id >= @Id", binding, transaction: transaction, commandTimeout: 30);
        var rows = await connection.QueryAsync<TaskRow>("SELECT id, title, note FROM f002_tasks WHERE id >= @Id ORDER BY id", binding, transaction: transaction, commandTimeout: 30);
        return new TaskReport { Count = count, Rows = rows.ToList() };
    }
}
