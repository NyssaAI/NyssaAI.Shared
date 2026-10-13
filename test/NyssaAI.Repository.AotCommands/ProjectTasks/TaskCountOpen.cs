using System.Data.Common;
using Dapper;
using NyssaAI.Repository.Commands;
using NyssaAI.Results;

namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Counts the open tasks in a project.</summary>
public sealed class TaskCountOpen : IRepoCommand<ProjectTaskParameters, long>
{
    /// <summary>Creates the command with the supplied parameters.</summary>
    public TaskCountOpen(ProjectTaskParameters parameters) => Parameters = parameters;

    /// <inheritdoc />
    public RepoCommandKey<ProjectTaskParameters, long> Key => ProjectTaskOperations.CountOpen;

    /// <inheritdoc />
    public ProjectTaskParameters Parameters { get; }

    async Task<Result<long>> IRepoCommand<ProjectTaskParameters, long>.ExecuteCoreAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        try
        {
            var binding = new Binding
            {
                ProjectId = Parameters.ProjectId,
                CancellationToken = cancellationToken
            };
            var value = await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM tutorial_tasks WHERE project_id = @ProjectId AND is_completed = FALSE",
                binding, transaction: transaction, commandTimeout: 30).ConfigureAwait(false);
            return Result<long>.Success(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return Result<long>.Failure(Error.FromMessage("The database returned an invalid value.", ErrorCategory.Validation));
        }
    }

    // CancellationToken is execution metadata; the generated factory does not bind it as SQL data.
    internal sealed record Binding
    {
        public required long ProjectId { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
