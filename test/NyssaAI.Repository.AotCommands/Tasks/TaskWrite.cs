using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskWrite Concrete application-owned generated operation.</summary>
public sealed class TaskWrite : IRepoCommand<TaskParameters, int>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskWrite(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, int> Key => TaskOperations.Write;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<int> IRepoCommand<TaskParameters, int>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var value = await connection.ExecuteAsync("INSERT INTO f002_tasks (id, title, note) VALUES (@Id, @Title, @Note)", binding, transaction: transaction, commandTimeout: 1);
        return value;
    }
}
