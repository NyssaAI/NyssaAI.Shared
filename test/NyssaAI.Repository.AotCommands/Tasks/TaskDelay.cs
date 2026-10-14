using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskDelay Concrete application-owned generated operation.</summary>
public sealed class TaskDelay : IRepoCommand<TaskParameters, int>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskDelay(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, int> Key => TaskOperations.Delay;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<int> IRepoCommand<TaskParameters, int>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var value = await connection.ExecuteAsync("/*f002-inflight*/ SELECT pg_sleep(@Id)", binding, transaction: transaction, commandTimeout: 30);
        return value;
    }
}
