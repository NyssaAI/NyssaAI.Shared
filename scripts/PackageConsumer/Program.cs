using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NyssaAI.DbGateway;
using NyssaAI.DbGateway.Commands;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.Logging.Configuration;

var directory = Directory.CreateTempSubdirectory("nyssaai-package-consumer-").FullName;
try
{
    var logPath = Path.Combine(directory, "application.jsonl");
    var services = new ServiceCollection();
    services.AddNyssaLogging(logPath)
        .WithRotatingJsonLog(Path.Combine(directory, "chained.jsonl"))
        .WithStdErr()
        .WithConsole();
    services.AddNyssaDbGateway(new DbGatewayConfiguration
    {
        DatabaseProvider = "sqlite",
        ConnectionString = "Data Source=:memory:"
    });

    await using (var container = services.BuildServiceProvider())
    {
        var gateway = container.GetRequiredService<IDbGateway>();
        var answer = await gateway.ExecuteAsync(new EchoCommand { Value = 42 });
        if (answer != 42)
            throw new InvalidOperationException("The packaged command returned the wrong value.");
        container.GetRequiredService<ILoggerFactory>().CreateLogger("Consumer")
            .LogInformation("Read answer {Answer}", answer);
    }

    using (var entry = JsonDocument.Parse(File.ReadAllLines(logPath).Single()))
    {
        if (entry.RootElement.GetProperty("AdditionalFields").GetProperty("Answer").GetInt32() != 42)
            throw new InvalidOperationException("The packaged logger lost structured data.");
        var datedPath = Directory.GetFiles(directory, "*-chained.jsonl").Single();
        if (File.ReadAllLines(datedPath).Length != 1)
            throw new InvalidOperationException("The packaged logging chain did not write the dated file.");
        Console.WriteLine("Package consumer passed: typed SQLite command and structured file logging.");
    }
}
finally
{
    Directory.Delete(directory, recursive: true);
}

internal sealed record EchoCommand : IDbCommand<int>
{
    public required int Value { get; init; }

    public string GetSql(DbDialect dialect) => "SELECT @value";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect) =>
        parameters.Add("@value", Value);

    public async ValueTask<int> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
    {
        await reader.ReadAsync(cancellationToken);
        return reader.GetInt32(0);
    }
}
