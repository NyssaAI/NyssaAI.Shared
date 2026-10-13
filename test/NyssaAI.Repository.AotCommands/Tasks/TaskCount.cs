using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;
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
    async Task<Result<long>> IRepoCommand<TaskParameters, long>.ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = TaskBinding.From(Parameters, cancellationToken);
            var value = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM f002_tasks WHERE id >= @Id", binding, transaction: transaction, commandTimeout: 30);
            return Result<long>.Success(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            // Translate only the concrete database-read boundary; unrelated author exceptions remain visible.
            return Result<long>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }
}
