using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.Migrations;
using NyssaAI.DbGateway.Providers;
using NyssaAI.DbGateway.UseCaseTests.Domain;
using NyssaAI.DbGateway.UseCaseTests.Fixtures;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.DbGateway.AotSmoke;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var timer = Stopwatch.StartNew();
        var allocated = GC.GetTotalAllocatedBytes();
        try
        {
            if (args.Length == 2 && args[0] == "verify")
                return await VerifyProviderAsync(args[1]).ConfigureAwait(false);
            if (args.Length < 2 || args[0] is not ("setup" or "run" or "switch"))
            {
                Console.Error.WriteLine("Usage: verify <provider> | setup <directory> <provider> | run <directory> | switch <directory>");
                return 2;
            }
            var encodedKey = Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_KEY")
                ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_KEY");
            if (string.IsNullOrWhiteSpace(encodedKey))
                return Fail("A valid smoke-test encryption key is required.");
            using (var configurationHost = new ConfigurationHost(encodedKey))
            {
                ConfigurationStore configuration = configurationHost.Open(args[1]);
                if (args[0] == "setup")
                {
                    if (args.Length != 3)
                        return 2;
                    var connectionString = args[2] == "postgresql"
                        ? Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_POSTGRES")
                            ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
                        : Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_CONNECTION")
                            ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_CONNECTION");
                    await configuration.SetDbGatewayConfigurationAsync(new DbGatewayConfiguration
                    {
                        DatabaseProvider = args[2],
                        ConnectionString = connectionString ?? string.Empty
                    }, storageProvider: "aesLocal").ConfigureAwait(false);
                    Console.WriteLine("Protected settings saved.");
                    return 0;
                }
                if (args[0] == "switch")
                    return await SwitchAsync(configuration).ConfigureAwait(false);
                return await RunAsync(configuration).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail("The smoke-test environment could not be used.");
        }
        finally
        {
            using (var process = Process.GetCurrentProcess())
            {
                Console.WriteLine($"elapsed_main_ms={timer.Elapsed.TotalMilliseconds:F3}; allocated_bytes={GC.GetTotalAllocatedBytes() - allocated}; working_set_bytes={process.WorkingSet64}");
            }
        }
    }

    private static async Task<int> VerifyProviderAsync(string providerName)
    {
        var connectionString = providerName == "postgresql"
            ? Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_POSTGRES")
                ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
            : Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_CONNECTION")
                ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return Fail($"Missing connection string for {providerName}.");

        using var workspace = new DatabaseWorkspace();
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = providerName,
                ConnectionString = connectionString
            },
            migrations: DatabaseWorkspace.TaskCatalog);

        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IDbProvider>();
        await workspace.ResetSchemaAsync(provider, CancellationToken.None).ConfigureAwait(false);

        var migrations = container.GetRequiredService<IMigrationRunner>();
        await migrations.MigrateAsync("1.1.0").ConfigureAwait(false);

        var gateway = container.GetRequiredService<IDbGateway>();
        var taskId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await gateway.ExecuteAsync(new TaskCreateCommand
        {
            Id = taskId,
            ProjectId = 1L,
            Title = "AOT smoke test",
            IsCompleted = false,
            CreatedAt = createdAt,
            Note = "native"
        }).ConfigureAwait(false);

        var loaded = await gateway.ExecuteAsync(new TaskGetByIdCommand { Id = taskId }).ConfigureAwait(false);
        if (loaded is null || loaded.Id != taskId || loaded.IsCompleted)
            return Fail("AOT task roundtrip failed.");

        Console.WriteLine($"provider={providerName}; aot_roundtrip=passed");
        return 0;
    }

    private static async Task<int> RunAsync(ConfigurationStore configuration)
    {
        var services = new ServiceCollection();
        await services.AddNyssaDbGatewayAsync(configuration).ConfigureAwait(false);
        await using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IDbProvider>();
        return await CheckConnectionAsync(provider).ConfigureAwait(false);
    }

    private static async Task<int> SwitchAsync(ConfigurationStore configuration)
    {
        await configuration.SetDbGatewayConfigurationAsync(new DbGatewayConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = "Data Source=:memory:"
        }, storageProvider: "aesLocal").ConfigureAwait(false);
        var firstSettings = await configuration.GetDbGatewayConfigurationAsync().ConfigureAwait(false);
        var firstServices = new ServiceCollection();
        firstServices.AddNyssaDbGateway(firstSettings);
        await using var firstContainer = firstServices.BuildServiceProvider();
        var first = firstContainer.GetRequiredService<IDbProvider>();

        var pgConn = Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_POSTGRES")
            ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
            ?? string.Empty;
        await configuration.SetDbGatewayConfigurationAsync(new DbGatewayConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = pgConn
        }).ConfigureAwait(false);
        var secondSettings = await configuration.GetDbGatewayConfigurationAsync().ConfigureAwait(false);
        var secondServices = new ServiceCollection();
        secondServices.AddNyssaDbGateway(() => secondSettings);
        await using var secondContainer = secondServices.BuildServiceProvider();
        var second = secondContainer.GetRequiredService<IDbProvider>();

        if (first.DatabaseProvider != "sqlite" || second.DatabaseProvider != "postgresql")
            return Fail("Provider snapshots were not preserved.");
        if (await CheckConnectionAsync(first).ConfigureAwait(false) != 0 || await CheckConnectionAsync(second).ConfigureAwait(false) != 0)
            return 1;
        Console.WriteLine("Stored selection changed; existing provider kept its snapshot.");
        return 0;
    }

    private static async Task<int> CheckConnectionAsync(IDbProvider provider)
    {
        await using var connection = await provider.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        if (Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false)) != 1)
            return Fail("The database check failed.");
        Console.WriteLine($"provider={provider.DatabaseProvider}; query=passed");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
