using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskCount Concrete application-owned generated operation.</summary>
public sealed class TaskCount : IRepoCommand<TaskParameters, long>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskCount(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, long> Key => TaskOperations.Count;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<long> IRepoCommand<TaskParameters, long>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var value = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM f002_tasks WHERE id >= @Id", binding, transaction: transaction, commandTimeout: 30);
        return value;
    }
}
