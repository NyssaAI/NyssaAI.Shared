using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskList Concrete application-owned generated operation.</summary>
public sealed class TaskList : IRepoCommand<TaskParameters, IReadOnlyList<TaskRow>>
{
    /// <summary>Creates the command for the supplied values.</summary>
    public TaskList(TaskParameters parameters) => Parameters = parameters;
    /// <inheritdoc />
    public RepoCommandKey<TaskParameters, IReadOnlyList<TaskRow>> Key => TaskOperations.List;
    /// <inheritdoc />
    public TaskParameters Parameters { get; }
    async Task<IReadOnlyList<TaskRow>> IRepoCommand<TaskParameters, IReadOnlyList<TaskRow>>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var binding = TaskBinding.From(Parameters, cancellationToken);
        var value = await connection.QueryAsync<TaskRow>("SELECT id, title, note FROM f002_tasks WHERE id >= @Id AND id < @Id + 100 ORDER BY id", binding, transaction: transaction, commandTimeout: 30);
        return value.ToList();
    }
}
