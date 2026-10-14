using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskDelete Concrete application-owned generated operation.</summary>
public sealed class TaskDelete : IRepoCommand<TaskParameters, int>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskDelete(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, int> Key => TaskOperations.Delete;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<int> IRepoCommand<TaskParameters, int>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var value = await connection.ExecuteAsync("DELETE FROM f002_tasks WHERE id = @Id", binding, transaction: transaction, commandTimeout: 30);
        return value;
    }
}
