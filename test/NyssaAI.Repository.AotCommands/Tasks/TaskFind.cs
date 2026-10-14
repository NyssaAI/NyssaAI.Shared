using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
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
    async Task<TaskRow> IRepoCommand<TaskParameters, TaskRow>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        #if REPOSITORY_NEGATIVE_PROBE
        var binding = Parameters;
        #else
        var binding = TaskBinding.From(Parameters, cancellationToken);
        #endif
        var rows = (await connection.QueryAsync<TaskRow>("SELECT id, title, note FROM f002_tasks WHERE title = @Title", binding, transaction: transaction, commandTimeout: 30)).ToList();
        return rows.Count switch
        {
            0 => throw new KeyNotFoundException("Task not found."),
            1 => rows[0],
            _ => throw new InvalidOperationException("Exactly one task is required.")
        };
    }
}
