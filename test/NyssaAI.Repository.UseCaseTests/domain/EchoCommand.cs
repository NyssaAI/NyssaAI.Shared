using System.Data.Common;
using NyssaAI.Repository.Commands;

namespace NyssaAI.Repository.UseCaseTests.domain;

public sealed class EchoCommand : IRepoCommand<int, int>
{
    internal EchoCommand(RepoCommandKey<int, int> key, int parameters)
    {
        Key = key;
        Parameters = parameters;
    }

    public RepoCommandKey<int, int> Key { get; }
    public int Parameters { get; }

    public async Task<int> ExecuteCoreAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken
    )
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT @value";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@value";
            parameter.Value = Parameters;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
    }
}
