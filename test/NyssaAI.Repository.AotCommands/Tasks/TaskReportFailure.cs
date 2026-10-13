using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskReportFailure Failure fixture: its first query succeeds and its second query addresses an absent table.</summary>
public sealed class TaskReportFailure : IRepoCommand<TaskParameters, TaskReport>
{
    /// <summary>Creates the command.</summary>
    public TaskReportFailure(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, TaskReport> Key => TaskOperations.ReportFailure;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<Result<TaskReport>> IRepoCommand<TaskParameters, TaskReport>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = TaskBinding.From(Parameters, cancellationToken);
            var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM f002_tasks WHERE id >= @Id", binding, transaction: transaction, commandTimeout: 30);
            var rows = await connection.QueryAsync<TaskRow>("SELECT id, title, note FROM f002_absent WHERE id >= @Id", binding, transaction: transaction, commandTimeout: 30);
            return Result<TaskReport>.Success(new TaskReport { Count = count, Rows = rows.ToList() });
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            // Translate only the concrete database-read boundary; unrelated author exceptions remain visible.
            return Result<TaskReport>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }
}
