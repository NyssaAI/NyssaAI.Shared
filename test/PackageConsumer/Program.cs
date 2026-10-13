using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.Logging.Configuration;
using NyssaAI.Repository;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

var directory = Directory.CreateTempSubdirectory("nyssaai-package-consumer-").FullName;
try
{
    var logPath = Path.Combine(directory, "application.jsonl");
    var services = new ServiceCollection();
    services.AddNyssaLogging(logPath)
        .WithRotatingJsonLog(Path.Combine(directory, "chained.jsonl"))
        .WithStdErr()
        .WithConsole();
    services.AddNyssaRepository(new RepositoryConfiguration
    {
        DatabaseProvider = "sqlite",
        ConnectionString = "Data Source=:memory:"
    }, commands:
    [
        RepoCommandRegistration.Create<int, int>("sqlite", EchoCommand.Operation, value =>
            Result<IRepoCommand<int, int>>.Success(new EchoCommand(value))).ThrowIfError()
    ]).ThrowIfError();

    await using (var container = services.BuildServiceProvider())
    {
        var repository = container.GetRequiredService<IRepository>();
        await using var session = (await repository.OpenSessionAsync()).ThrowIfError();
        var command = repository.CommandRegistry(EchoCommand.Operation, 42).ThrowIfError();
        var answer = (await session.ExecuteAsync(command)).ThrowIfError();
        if (answer != 42) throw new InvalidOperationException("The packaged command returned the wrong value.");
        container.GetRequiredService<ILoggerFactory>().CreateLogger("Consumer")
            .LogInformation("Read answer {Answer}", answer);
    }

    using var entry = JsonDocument.Parse(File.ReadAllLines(logPath).Single());
    if (entry.RootElement.GetProperty("AdditionalFields").GetProperty("Answer").GetInt32() != 42)
        throw new InvalidOperationException("The packaged logger lost structured data.");
    var datedPath = Directory.GetFiles(directory, "*-chained.jsonl").Single();
    if (File.ReadAllLines(datedPath).Length != 1)
        throw new InvalidOperationException("The packaged logging chain did not write the dated file.");
    Console.WriteLine("Package consumer passed: typed SQLite command and structured file logging.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

internal sealed class EchoCommand(int value) : IRepoCommand<int, int>
{
    public static RepoCommandKey<int, int> Operation { get; } = new() { Name = "consumer.echo" };
    public RepoCommandKey<int, int> Key => Operation;
    public int Parameters => value;

    public async Task<Result<int>> ExecuteCoreAsync(DbConnection connection, DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT @value";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "value";
        parameter.Value = Parameters;
        command.Parameters.Add(parameter);
        var answer = await command.ExecuteScalarAsync(cancellationToken);
        return Result<int>.Success(Convert.ToInt32(answer));
    }
}
