using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskFind Concrete application-owned generated operation.</summary>
public sealed class TaskFind : IRepoCommand<TaskParameters, TaskRow>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskFind(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, TaskRow> Key => TaskOperations.Find;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    #if REPOSITORY_NEGATIVE_PROBE
    [DapperAot(false)]
    #endif
    async Task<Result<TaskRow>> IRepoCommand<TaskParameters, TaskRow>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            #if REPOSITORY_NEGATIVE_PROBE
            var binding = Parameters;
            #else
            var binding = TaskBinding.From(Parameters, cancellationToken);
            #endif
            var rows = (await connection.QueryAsync<TaskRow>("SELECT id, title, note FROM f002_tasks WHERE title = @Title", binding, transaction: transaction, commandTimeout: 30)).ToList();
            return rows.Count switch
            {
                0 => Result<TaskRow>.Failure(Error.FromMessage("Task not found.", ErrorCategory.NotFound)),
                1 => Result<TaskRow>.Success(rows[0]),
                _ => Result<TaskRow>.Failure(Error.FromMessage("Exactly one task is required.", ErrorCategory.Validation))
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            // Translate only the concrete database-read boundary; unrelated author exceptions remain visible.
            return Result<TaskRow>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }
}
